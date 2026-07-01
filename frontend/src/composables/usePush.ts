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

export function usePush() {
  const pushSupported = 'serviceWorker' in navigator && 'PushManager' in window
  const pushState = ref<'unknown' | 'subscribed' | 'unsubscribed' | 'denied'>('unknown')

  async function initPushState() {
    if (!pushSupported) return
    const perm = Notification.permission
    if (perm === 'denied') {
      pushState.value = 'denied'
      return
    }
    const reg = await navigator.serviceWorker.ready
    const sub = await reg.pushManager.getSubscription()
    pushState.value = sub ? 'subscribed' : 'unsubscribed'
  }

  async function togglePush() {
    if (!pushSupported) return
    const reg = await navigator.serviceWorker.ready

    if (pushState.value === 'subscribed') {
      const sub = await reg.pushManager.getSubscription()
      if (sub) {
        await pushApi.unsubscribe(sub.endpoint)
        await sub.unsubscribe()
      }
      pushState.value = 'unsubscribed'
      return
    }

    try {
      const { publicKey } = await pushApi.getVapidPublicKey()
      const sub = await reg.pushManager.subscribe({
        userVisibleOnly: true,
        applicationServerKey: urlBase64ToUint8Array(publicKey),
      })
      const json = sub.toJSON()
      const p256dh = json.keys?.['p256dh'] ?? ''
      const auth = json.keys?.['auth'] ?? ''
      await pushApi.subscribe(sub.endpoint, p256dh, auth)
      pushState.value = 'subscribed'
    } catch {
      if (Notification.permission === 'denied') pushState.value = 'denied'
    }
  }

  onMounted(initPushState)

  return { pushSupported, pushState, togglePush }
}
