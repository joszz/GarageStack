import { afterEach, describe, it, expect, vi } from 'vitest'
import { defineComponent, ref } from 'vue'
import { flushPromises, mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { createMemoryHistory, createRouter } from 'vue-router'
import App from '../App.vue'
import { resetConnection } from '@/composables/useConnection'
import { setBrowserOnline } from '@/composables/__tests__/connectionStub'

const session = vi.hoisted(() => ({ authenticated: false }))

// Only what the app reads of this device's settings: whether swiping the menu is switched on.
const ui = vi.hoisted(() => ({ edgeSwipeMenu: false }))

vi.mock('@/stores/settingsUi', () => ({ useUiSettingsStore: () => ui }))

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
  lastUpdated: null as Date | null,
}))
// What the routed page is keyed on, reactive so a test can play the list arriving or a switch.
const live = vi.hoisted(() => ({
  state: null as { activeVin: string | null; switchCount: number } | null,
}))

vi.mock('@/stores/vehicle', async () => {
  const { reactive } = await import('vue')
  live.state = reactive({ activeVin: null, switchCount: 0 })
  const state = live.state
  return {
    useVehicleStore: () => ({
      currentStatus: vehicle.status,
      activeVehicle: vehicle.active,
      get activeVin() {
        return state.activeVin
      },
      get switchCount() {
        return state.switchCount
      },
      vehicles: [],
      hasMultipleVehicles: false,
      effectiveVehicleType: vehicle.type,
      loading: false,
      lastUpdated: vehicle.lastUpdated,
    }),
  }
})

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

const refreshing = vi.hoisted(() => ({
  available: true,
  refresh: vi.fn<() => void>(),
}))

vi.mock('@/composables/useRefresh', async () => {
  const { computed } = await import('vue')
  return {
    useRefresh: () => ({
      canRefresh: computed(() => refreshing.available),
      refreshing: computed(() => false),
      disabled: computed(() => false),
      refresh: refreshing.refresh,
    }),
  }
})

vi.mock('@/composables/useFavicon', () => ({ useFavicon: vi.fn<() => void>() }))
vi.mock('@/composables/useScreenWakeLock', () => ({ useScreenWakeLock: vi.fn<() => void>() }))
vi.mock('@/composables/usePhoneGpsRecorder', () => ({ usePhoneGpsRecorder: vi.fn<() => void>() }))

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

describe('App menu swipe', () => {
  // Phone-sized, with a touchscreen: what the swipe is offered on.
  function onATouchPhone() {
    vi.stubGlobal('matchMedia', (query: string) => ({
      matches: query === '(width <= 767px)' || query === '(any-pointer: coarse)',
      media: query,
      addEventListener: () => {},
      removeEventListener: () => {},
    }))
  }

  async function mountClosed(path = '/') {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/', component: Page },
        { path: '/map', name: 'map', component: Page },
        { path: '/login', name: 'login', component: Page },
      ],
    })
    await router.push(path)
    return mount(App, {
      attachTo: document.body,
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
  }

  function swipe(target: Element, x: number, dx: number) {
    const init = { bubbles: true, isPrimary: true, pointerType: 'touch', pointerId: 1 }
    target.dispatchEvent(new PointerEvent('pointerdown', { ...init, clientX: x, clientY: 300 }))
    target.dispatchEvent(
      new PointerEvent('pointermove', { ...init, clientX: x + dx, clientY: 300 }),
    )
  }

  afterEach(() => {
    vi.unstubAllGlobals()
    ui.edgeSwipeMenu = false
    document.body.innerHTML = ''
  })

  it('opens the menu for a swipe from the left edge, and closes it for one back', async () => {
    onATouchPhone()
    ui.edgeSwipeMenu = true
    const wrapper = await mountClosed()
    expect(wrapper.find('.sidebar--open').exists()).toBe(false)

    swipe(wrapper.get('.app-body').element, 40, 100)
    await flushPromises()
    expect(wrapper.find('.sidebar--open').exists()).toBe(true)
    expect(wrapper.get('.hamburger').attributes('aria-expanded')).toBe('true')

    swipe(wrapper.get('.sidebar').element, 200, -100)
    await flushPromises()
    expect(wrapper.find('.sidebar--open').exists()).toBe(false)
  })

  it('leaves the menu shut while this device has not asked for the swipe', async () => {
    onATouchPhone()
    const wrapper = await mountClosed()

    swipe(wrapper.get('.app-body').element, 40, 100)
    await flushPromises()

    expect(wrapper.find('.sidebar--open').exists()).toBe(false)
  })

  it('leaves the menu shut on a screen without a touchscreen, whatever the setting says', async () => {
    ui.edgeSwipeMenu = true
    const wrapper = await mountClosed()

    swipe(wrapper.get('.app-body').element, 40, 100)
    await flushPromises()

    expect(wrapper.find('.sidebar--open').exists()).toBe(false)
  })

  // The map pans on any touch, so on its page the swipe does not open the menu.
  it('does not open the menu on the map page', async () => {
    onATouchPhone()
    ui.edgeSwipeMenu = true
    const wrapper = await mountClosed('/map')

    swipe(wrapper.get('.app-body').element, 40, 100)
    await flushPromises()

    expect(wrapper.find('.sidebar--open').exists()).toBe(false)
  })

  it('still closes a menu opened by its button on the map page', async () => {
    onATouchPhone()
    ui.edgeSwipeMenu = true
    const wrapper = await mountClosed('/map')
    await wrapper.get('.hamburger').trigger('click')
    expect(wrapper.find('.sidebar--open').exists()).toBe(true)

    swipe(wrapper.get('.sidebar').element, 200, -100)
    await flushPromises()

    expect(wrapper.find('.sidebar--open').exists()).toBe(false)
  })
})

describe('App top bar', () => {
  // On a phone the page's own buttons and then Settings land in the slots; with the bell they form
  // one evenly spaced group, the slots coming first so the bell stays at the far end.
  it('keeps the bell in one group with the teleported buttons, after them', async () => {
    const wrapper = await mountAt('/')

    const group = wrapper.find('.mobile-topbar__actions')
    const children = group.element.children
    expect(children[0]?.id).toBe('mobile-topbar-page-actions')
    expect(children[1]?.id).toBe('mobile-topbar-actions')
    expect(children[children.length - 1]?.classList.contains('notif-bell')).toBe(true)
  })

  // The page's heading takes the room the app's car icon had: home is in the menu.
  it('gives the page heading the bar beside the menu button', async () => {
    const wrapper = await mountAt('/')

    const bar = wrapper.get('.mobile-topbar').element.children
    expect(bar[0]?.classList.contains('hamburger')).toBe(true)
    expect(bar[1]?.id).toBe('mobile-topbar-title')
  })
})

describe('App fetched line', () => {
  function screenIsPhone(phone: boolean) {
    vi.stubGlobal('matchMedia', (query: string) => ({
      matches: phone && query === '(width <= 767px)',
      media: query,
      addEventListener: () => {},
      removeEventListener: () => {},
    }))
  }

  afterEach(() => {
    vi.unstubAllGlobals()
    vehicle.lastUpdated = null
    refreshing.available = true
    refreshing.refresh.mockClear()
  })

  // The footer and its Refresh button are hidden on a phone: the time it was fetched renews it.
  it('refreshes from the fetched line on a phone, its icon in the place the others have', async () => {
    screenIsPhone(true)
    vehicle.lastUpdated = new Date('2026-11-26T08:34:00')
    const wrapper = await mountAt('/')

    const line = wrapper.get('.sidebar-timestamp--refresh')
    expect(line.element.tagName).toBe('BUTTON')
    expect(line.attributes('aria-label')).toContain('control.refreshFetched')
    expect(line.element.firstElementChild?.classList.contains('sidebar-timestamp__icon')).toBe(true)

    await line.trigger('click')
    expect(refreshing.refresh).toHaveBeenCalledOnce()
  })

  it('keeps the fetched line a plain line off a phone, where the footer refreshes', async () => {
    screenIsPhone(false)
    vehicle.lastUpdated = new Date('2026-11-26T08:34:00')
    const wrapper = await mountAt('/')

    expect(wrapper.find('.sidebar-timestamp--refresh').exists()).toBe(false)
    expect(wrapper.text()).toContain('common.fetched')
  })

  // No command reaches the car without the server, so the line offers none meanwhile.
  it('keeps the fetched line a plain line without the server', async () => {
    screenIsPhone(true)
    refreshing.available = false
    vehicle.lastUpdated = new Date('2026-11-26T08:34:00')
    const wrapper = await mountAt('/')

    expect(wrapper.find('.sidebar-timestamp--refresh').exists()).toBe(false)
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

describe('App routed page', () => {
  afterEach(() => {
    live.state!.activeVin = null
    live.state!.switchCount = 0
  })

  async function mountCounting() {
    let mounts = 0
    const Counted = defineComponent({
      mounted: () => {
        mounts += 1
      },
      render: () => null,
    })
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/', component: Counted }],
    })
    await router.push('/')
    mount(App, {
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
    await flushPromises()
    return () => mounts
  }

  // After a sign-in the vehicle list arrives once the page is already up: a one-car install must
  // not mount it a second time for that.
  it('mounts the page once when the car becomes known after a sign-in', async () => {
    const mounts = await mountCounting()

    live.state!.activeVin = 'FAKEVN00000000001'
    await flushPromises()

    expect(mounts()).toBe(1)
  })

  it('mounts the page afresh when the user picks another car', async () => {
    live.state!.activeVin = 'FAKEVN00000000001'
    const mounts = await mountCounting()

    live.state!.activeVin = 'FAKEVN00000000002'
    live.state!.switchCount += 1
    await flushPromises()

    expect(mounts()).toBe(2)
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
