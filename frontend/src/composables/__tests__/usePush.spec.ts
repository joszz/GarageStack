import { describe, it, expect, beforeEach, vi, afterEach } from 'vitest'
import { defineComponent, h, nextTick } from 'vue'
import { mount } from '@vue/test-utils'
import { pushApi } from '@/services/pushApi'
import { usePush } from '@/composables/usePush'

vi.mock('@/services/pushApi', () => ({
  pushApi: {
    getVapidPublicKey: vi.fn<() => Promise<{ publicKey: string }>>(),
    subscribe: vi.fn<(e: string, p: string, a: string) => Promise<void>>(),
    unsubscribe: vi.fn<(e: string) => Promise<void>>(),
  },
}))

const getVapidPublicKeyMock = vi.mocked(pushApi.getVapidPublicKey)
const subscribeMock = vi.mocked(pushApi.subscribe)
const unsubscribeMock = vi.mocked(pushApi.unsubscribe)

// Base64url with no padding, the shape a real VAPID public key arrives in.
const VAPID_KEY =
  'BNcRdreALRFXTkOOUHK1EtK2wtaz5Ry4YfYCA_0QTpQtUbVlUls0VJXg7A8u-Ts1XbjhazAkj7I99e8QcYP7DkM'

function makeSubscription(endpoint = 'https://push.example/abc') {
  return {
    endpoint,
    unsubscribe: vi.fn<() => Promise<boolean>>().mockResolvedValue(true),
    toJSON: () => ({ endpoint, keys: { p256dh: 'p256dh-key', auth: 'auth-key' } }),
  } as unknown as PushSubscription & { unsubscribe: ReturnType<typeof vi.fn> }
}

type PushManagerStub = {
  getSubscription: ReturnType<typeof vi.fn>
  subscribe: ReturnType<typeof vi.fn>
}

let pushManager: PushManagerStub

function setPermission(permission: NotificationPermission) {
  vi.stubGlobal('Notification', { permission })
}

// usePush registers initPushState with onMounted, so the composable has to run inside a real
// component for the load-time path to be exercised at all.
function mountUsePush() {
  const state: { api: ReturnType<typeof usePush> | null } = { api: null }
  const wrapper = mount(
    defineComponent({
      setup() {
        state.api = usePush()
        return () => h('div')
      },
    }),
  )
  return { wrapper, api: () => state.api! }
}

// serviceWorker.ready resolves on the microtask queue, and initPushState awaits several
// promises after it. Flushing timers would not help; draining the queue does.
async function flush() {
  for (let i = 0; i < 12; i++) await nextTick()
}

beforeEach(() => {
  pushManager = {
    getSubscription: vi.fn<() => Promise<PushSubscription | null>>().mockResolvedValue(null),
    subscribe: vi.fn<() => Promise<PushSubscription>>(),
  }

  vi.stubGlobal('PushManager', class PushManager {})
  Object.defineProperty(navigator, 'serviceWorker', {
    configurable: true,
    value: { ready: Promise.resolve({ pushManager } as unknown as ServiceWorkerRegistration) },
  })

  setPermission('default')
  getVapidPublicKeyMock.mockResolvedValue({ publicKey: VAPID_KEY })
  subscribeMock.mockResolvedValue(undefined)
  unsubscribeMock.mockResolvedValue(undefined)
})

afterEach(() => {
  vi.unstubAllGlobals()
  vi.clearAllMocks()
})

describe('usePush on load', () => {
  it('re-registers an existing browser subscription with the server', async () => {
    // The server can have lost the row (a wiped database, or a subscribe call that never
    // arrived) while the browser still holds the subscription. Trusting getSubscription() alone
    // is what let the UI claim push was on while nothing was ever delivered.
    const sub = makeSubscription()
    pushManager.getSubscription.mockResolvedValue(sub)
    setPermission('granted')

    const { api } = mountUsePush()
    await flush()

    expect(subscribeMock).toHaveBeenCalledWith('https://push.example/abc', 'p256dh-key', 'auth-key')
    expect(pushManager.subscribe).not.toHaveBeenCalled()
    expect(api().pushState.value).toBe('subscribed')
  })

  it('subscribes silently when permission is already granted but no subscription exists', async () => {
    // The reinstalled-PWA case: permission survives, the subscription does not, and subscribing
    // again in that state never shows a prompt.
    const sub = makeSubscription()
    pushManager.subscribe.mockResolvedValue(sub)
    setPermission('granted')

    const { api } = mountUsePush()
    await flush()

    expect(pushManager.subscribe).toHaveBeenCalledOnce()
    expect(subscribeMock).toHaveBeenCalledWith('https://push.example/abc', 'p256dh-key', 'auth-key')
    expect(api().pushState.value).toBe('subscribed')
  })

  it('does not subscribe while permission is still default, to avoid an uninvited prompt', async () => {
    setPermission('default')

    const { api } = mountUsePush()
    await flush()

    expect(pushManager.subscribe).not.toHaveBeenCalled()
    expect(subscribeMock).not.toHaveBeenCalled()
    expect(api().pushState.value).toBe('unsubscribed')
  })

  it('reports denied without touching the push manager', async () => {
    setPermission('denied')

    const { api } = mountUsePush()
    await flush()

    expect(pushManager.getSubscription).not.toHaveBeenCalled()
    expect(api().pushState.value).toBe('denied')
  })

  it('reports unsubscribed, and no error, when the server is unreachable on load', async () => {
    const sub = makeSubscription()
    pushManager.getSubscription.mockResolvedValue(sub)
    setPermission('granted')
    subscribeMock.mockRejectedValue(new Error('API error 503'))

    const { api } = mountUsePush()
    await flush()

    expect(api().pushState.value).toBe('unsubscribed')
    // A load while the API happens to be down must not put an error under settings the user
    // never opened; the browser subscription stays, so the next load retries.
    expect(api().pushError.value).toBe(false)
    expect(sub.unsubscribe).not.toHaveBeenCalled()
  })

  it('replaces a subscription left over from an earlier VAPID key pair', async () => {
    const stale = makeSubscription('https://push.example/stale')
    const fresh = makeSubscription('https://push.example/fresh')
    setPermission('granted')
    // getSubscription() reports nothing on the first pass (the browser has one, but for a key
    // the server no longer uses), then hands over the stale one so it can be dropped.
    pushManager.getSubscription.mockResolvedValueOnce(null).mockResolvedValue(stale)
    pushManager.subscribe
      .mockRejectedValueOnce(new DOMException('already subscribed', 'InvalidStateError'))
      .mockResolvedValue(fresh)

    const { api } = mountUsePush()
    await flush()

    expect(stale.unsubscribe).toHaveBeenCalledOnce()
    expect(pushManager.subscribe).toHaveBeenCalledTimes(2)
    expect(subscribeMock).toHaveBeenCalledWith(
      'https://push.example/fresh',
      'p256dh-key',
      'auth-key',
    )
    expect(api().pushState.value).toBe('subscribed')
  })
})

describe('usePush togglePush', () => {
  it('rolls the browser subscription back when the server refuses it', async () => {
    // Without the rollback the browser keeps a subscription the server never recorded, and
    // every later load reports "subscribed" while nothing is ever delivered.
    const sub = makeSubscription()
    pushManager.subscribe.mockResolvedValue(sub)
    setPermission('granted')
    subscribeMock.mockRejectedValue(new Error('API error 403'))

    const { api } = mountUsePush()
    await flush()
    await api().togglePush()

    expect(sub.unsubscribe).toHaveBeenCalledOnce()
    expect(api().pushState.value).toBe('unsubscribed')
    expect(api().pushError.value).toBe(true)
  })

  it('reports denied rather than an error when the permission prompt is refused', async () => {
    setPermission('default')
    pushManager.subscribe.mockImplementation(() => {
      setPermission('denied')
      return Promise.reject(new DOMException('denied', 'NotAllowedError'))
    })

    const { api } = mountUsePush()
    await flush()
    await api().togglePush()

    expect(api().pushState.value).toBe('denied')
    expect(api().pushError.value).toBe(false)
    expect(subscribeMock).not.toHaveBeenCalled()
  })

  it('subscribes and clears a previous error', async () => {
    const sub = makeSubscription()
    pushManager.subscribe.mockResolvedValue(sub)
    setPermission('default')

    const { api } = mountUsePush()
    await flush()
    await api().togglePush()

    expect(subscribeMock).toHaveBeenCalledWith('https://push.example/abc', 'p256dh-key', 'auth-key')
    expect(api().pushState.value).toBe('subscribed')
    expect(api().pushError.value).toBe(false)
  })

  it('drops the local subscription even when the server cannot be told', async () => {
    const sub = makeSubscription()
    pushManager.getSubscription.mockResolvedValue(sub)
    setPermission('granted')

    const { api } = mountUsePush()
    await flush()
    expect(api().pushState.value).toBe('subscribed')

    unsubscribeMock.mockRejectedValue(new Error('API error 503'))
    await api().togglePush()

    expect(sub.unsubscribe).toHaveBeenCalledOnce()
    expect(api().pushState.value).toBe('unsubscribed')
  })
})
