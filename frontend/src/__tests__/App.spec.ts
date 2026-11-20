import { afterEach, describe, it, expect, vi } from 'vitest'
import { defineComponent, ref } from 'vue'
import { flushPromises, mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { createMemoryHistory, createRouter } from 'vue-router'
import App from '../App.vue'
import { resetConnection } from '@/composables/useConnection'
import { setBrowserOnline } from '@/composables/__tests__/connectionStub'

const session = vi.hoisted(() => ({ authenticated: false }))

vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({
    isAuthenticated: session.authenticated,
    username: 'demo',
    logout: vi.fn<() => void>(),
  }),
}))

const vehicle = vi.hoisted(() => ({
  type: 'bev',
  active: null as { id: number } | null,
  status: null as { isAvailable: boolean | null } | null,
}))

vi.mock('@/stores/vehicle', () => ({
  useVehicleStore: () => ({
    currentStatus: vehicle.status,
    activeVehicle: vehicle.active,
    effectiveVehicleType: vehicle.type,
    loading: false,
    lastUpdated: null,
  }),
}))

vi.mock('@/composables/useNotifications', () => ({
  prependNotification: vi.fn<() => void>(),
  useNotifications: () => ({
    notifications: ref([]),
    unreadCount: ref(0),
    panelOpen: ref(false),
    loading: ref(false),
    fetchError: ref(null),
    actionError: ref(null),
    fetchNotifications: vi.fn<() => void>(),
    togglePanel: vi.fn<() => void>(),
    closePanel: vi.fn<() => void>(),
    archiveNotification: vi.fn<() => void>(),
    archiveAllNotifications: vi.fn<() => void>(),
    deleteNotification: vi.fn<() => void>(),
    deleteAllNotifications: vi.fn<() => void>(),
  }),
}))

const signalR = vi.hoisted(() => ({ start: vi.fn<(vehicleId: number) => void>() }))

vi.mock('@/composables/useSignalR', () => ({
  useSignalR: () => ({
    connected: ref(false),
    start: signalR.start,
    stop: vi.fn<() => void>(),
  }),
}))

const gateway = vi.hoisted(() => ({ signedIn: null as boolean | null }))

vi.mock('@/services/gatewayApi', () => ({
  gatewayApi: { get: () => Promise.resolve({ signedIn: gateway.signedIn }) },
}))

vi.mock('@/composables/useBrowserNotifications', () => ({
  useBrowserNotifications: () => ({ show: vi.fn<() => void>() }),
}))

vi.mock('@/composables/useFavicon', () => ({ useFavicon: vi.fn<() => void>() }))

// Its template imports the app icon from public/, which only Vite resolves.
vi.mock('@/components/PwaInstallModal.vue', () => ({ default: { render: () => null } }))

const i18n = createI18n({ legacy: false, locale: 'en', missingWarn: false, fallbackWarn: false })
const Page = defineComponent({ render: () => null })

async function mountAt(path: string) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: Page },
      { path: '/map', component: Page },
      { path: '/login', name: 'login', component: Page },
    ],
  })
  await router.push(path)
  const wrapper = mount(App, {
    global: {
      plugins: [i18n, router],
      stubs: {
        FontAwesomeIcon: true,
        AppFooter: true,
        NotificationPanel: true,
        DemoBanner: true,
        PwaInstallModal: true,
      },
    },
  })
  await wrapper.find('.hamburger').trigger('click')
  expect(wrapper.find('.sidebar--open').exists()).toBe(true)
  return wrapper
}

describe('App mobile menu', () => {
  it('closes when the brand is tapped on the dashboard it links to', async () => {
    const wrapper = await mountAt('/')

    await wrapper.find('.sidebar-brand').trigger('click')
    await flushPromises()

    expect(wrapper.find('.sidebar--open').exists()).toBe(false)
    expect(wrapper.find('.hamburger').attributes('aria-expanded')).toBe('false')
  })

  it("closes when the current page's own entry is tapped", async () => {
    const wrapper = await mountAt('/map')

    await wrapper.find('.sidebar-nav a[href="/map"]').trigger('click')
    await flushPromises()

    expect(wrapper.find('.sidebar--open').exists()).toBe(false)
  })

  it('stays open for a button in the drawer', async () => {
    const wrapper = await mountAt('/')

    await wrapper.find('.sidebar-notif-btn').trigger('click')
    await flushPromises()

    expect(wrapper.find('.sidebar--open').exists()).toBe(true)
  })
})

describe('App top bar', () => {
  // Refresh and Settings land in the slot on a phone; with the bell they form one evenly spaced
  // group, the slot coming first so the bell stays at the far end.
  it('keeps the bell in one group with the teleported buttons, after them', async () => {
    const wrapper = await mountAt('/')

    const group = wrapper.find('.mobile-topbar__actions')
    const children = group.element.children
    expect(children[0]?.id).toBe('mobile-topbar-actions')
    expect(children[children.length - 1]?.classList.contains('notif-bell')).toBe(true)
  })
})

describe('App menu', () => {
  it('lists the charge history for a car that plugs in', async () => {
    vehicle.type = 'bev'
    const wrapper = await mountAt('/')

    expect(wrapper.find('.sidebar-nav a[href="/charge-history"]').exists()).toBe(true)
  })

  // A plain hybrid never charges from a plug.
  it('leaves the charge history out for a plain hybrid', async () => {
    vehicle.type = 'hev'
    const wrapper = await mountAt('/')

    expect(wrapper.find('.sidebar-nav a[href="/charge-history"]').exists()).toBe(false)
    vehicle.type = 'bev'
  })

  // Nothing of the server's log files is saved for offline use.
  it('leaves the log viewer out without the server, and lists it again once it is back', async () => {
    setBrowserOnline(false)
    const wrapper = await mountAt('/')
    expect(wrapper.find('.sidebar-nav a[href="/logs"]').exists()).toBe(false)

    vi.restoreAllMocks()
    resetConnection()
    await wrapper.vm.$nextTick()
    expect(wrapper.find('.sidebar-nav a[href="/logs"]').exists()).toBe(true)
  })
})

describe('App live updates', () => {
  afterEach(() => {
    session.authenticated = false
    vehicle.active = null
    signalR.start.mockClear()
  })

  // The vehicle store starts from the car this browser saved, so the car and the session are
  // there before anything changes; live updates must start all the same.
  it('starts for a car and a session that are there from the start', async () => {
    session.authenticated = true
    vehicle.active = { id: 7 }

    await mountAt('/')

    expect(signalR.start).toHaveBeenCalledWith(7)
  })

  it('does not start without a session', async () => {
    vehicle.active = { id: 7 }

    await mountAt('/')

    expect(signalR.start).not.toHaveBeenCalled()
  })
})

describe('App car status', () => {
  afterEach(() => {
    session.authenticated = false
    vehicle.active = null
    vehicle.status = null
    gateway.signedIn = null
  })

  async function statusText() {
    session.authenticated = true
    vehicle.active = { id: 7 }
    const wrapper = await mountAt('/')
    await flushPromises()
    // The last one: a "live updates paused" line above it shares the class.
    return wrapper.findAll('.sidebar-online-status').slice(-1)[0]!.text()
  }

  it('shows a car that is offline on opening as offline straight away', async () => {
    vehicle.status = { isAvailable: false }
    gateway.signedIn = true

    expect(await statusText()).toContain('common.offline')
  })

  // No car sends anything while the gateway cannot sign in, so that is what the app says.
  it('says the MG sign-in failed rather than that the car is offline', async () => {
    vehicle.status = { isAvailable: false }
    gateway.signedIn = false

    expect(await statusText()).toContain('vehicle.gatewaySignedOut')
  })
})
