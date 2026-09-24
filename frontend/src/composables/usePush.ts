import { ref, onMounted } from 'vue'
import { pushApi } from '@/services/pushApi'

function urlBase64ToUint8Array(base64String: string): Uint8Array<ArrayBuffer> {
  const padding = '='.repeat((4 - (base64String.length % 4)) % 4)
  const base64 = (base64String + padding).replace(/-/g, '+').replace(/_/g, '/')
  const raw = window.atob(base64)
  const arr = new Uint8Array(raw.length)
  for (let i = 0; i < raw.length; i++) arr[i] = raw.charCodeAt(i)
  return arr
}

export type PushState = 'unknown' | 'subscribed' | 'unsubscribed' | 'denied'

// Hands a browser subscription to the server. The API upserts on the endpoint, so sending one it
// already holds is a no-op and this can safely run on every load.
function registerWithServer(sub: PushSubscription): Promise<void> {
  const json = sub.toJSON()
  return pushApi.subscribe(sub.endpoint, json.keys?.['p256dh'] ?? '', json.keys?.['auth'] ?? '')
}

export function usePush() {
  const pushSupported = 'serviceWorker' in navigator && 'PushManager' in window
  const pushState = ref<PushState>('unknown')
  // Set when an explicit subscribe attempt failed for any reason other than a refused permission.
  // Without it the UI would just show "off" again and give no hint that anything went wrong.
  const pushError = ref(false)

  // Subscribes against the server's current VAPID key. A subscription left over from an earlier
  // key pair makes subscribe() reject with InvalidStateError, which would otherwise leave the
  // browser permanently unable to resubscribe: drop that one and retry instead.
  async function subscribeInBrowser(reg: ServiceWorkerRegistration): Promise<PushSubscription> {
    const { publicKey } = await pushApi.getVapidPublicKey()
    const options: PushSubscriptionOptionsInit = {
      userVisibleOnly: true,
      applicationServerKey: urlBase64ToUint8Array(publicKey),
    }

    try {
      return await reg.pushManager.subscribe(options)
    } catch (err) {
      if (!(err instanceof DOMException) || err.name !== 'InvalidStateError') throw err
      const stale = await reg.pushManager.getSubscription()
      await stale?.unsubscribe()
      return reg.pushManager.subscribe(options)
    }
  }

  async function initPushState() {
    if (!pushSupported) return
    if (Notification.permission === 'denied') {
      pushState.value = 'denied'
      return
    }

    const reg = await navigator.serviceWorker.ready

    try {
      let sub = await reg.pushManager.getSubscription()

      // Permission is already granted but there is no subscription: a reinstalled PWA, cleared
      // site data, or one the push service expired. Subscribing in that state never prompts, so
      // heal it silently. With permission still 'default' we leave it alone - asking uninvited on
      // load is worse than waiting for the user to open the notification settings.
      if (!sub && Notification.permission === 'granted') sub = await subscribeInBrowser(reg)

      if (!sub) {
        pushState.value = 'unsubscribed'
        return
      }

      // Having a browser subscription is not proof the server still knows about it: the row can
      // have been lost with the database, or never have arrived because the subscribe call
      // failed. Re-send it rather than taking the browser's word for it.
      await registerWithServer(sub)
      pushState.value = 'subscribed'
      pushError.value = false
    } catch {
      // Nothing is surfaced here: a load that happens while the API is down should not put an
      // error under the settings the user never touched. The subscription stays in the browser,
      // so the next load (or the next toggle) retries the hand-off. Permission is not re-read
      // either - it was 'granted' or 'default' above, and nothing on this path prompts.
      pushState.value = 'unsubscribed'
    }
  }

  async function togglePush() {
    if (!pushSupported) return
    const reg = await navigator.serviceWorker.ready

    if (pushState.value === 'subscribed') {
      const sub = await reg.pushManager.getSubscription()
      if (sub) {
        // Drop the local subscription even when the server could not be told: the push service
        // answers 410 for it afterwards and the Worker prunes the row on its next send.
        await pushApi.unsubscribe(sub.endpoint).catch(() => {})
        await sub.unsubscribe()
      }
      pushState.value = 'unsubscribed'
      pushError.value = false
      return
    }

    let sub: PushSubscription | null = null
    try {
      sub = await subscribeInBrowser(reg)
      await registerWithServer(sub)
      pushState.value = 'subscribed'
      pushError.value = false
    } catch {
      // The browser subscription is created before the server is told about it. Keeping one the
      // server never recorded is the worst outcome available: getSubscription() reports it on
      // every later load, the UI says push is on, and nothing is ever delivered.
      if (sub) await sub.unsubscribe().catch(() => {})
      const denied = Notification.permission === 'denied'
      pushState.value = denied ? 'denied' : 'unsubscribed'
      pushError.value = !denied
    }
  }

  onMounted(initPushState)

  return { pushSupported, pushState, pushError, togglePush }
}
