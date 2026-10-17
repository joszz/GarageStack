import { describe, it, expect, vi } from 'vitest'
import { defineComponent, ref } from 'vue'
import { flushPromises, mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { createMemoryHistory, createRouter } from 'vue-router'
import App from '../App.vue'

vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({ isAuthenticated: false, username: 'demo', logout: vi.fn<() => void>() }),
}))

vi.mock('@/stores/vehicle', () => ({
  useVehicleStore: () => ({
    currentStatus: null,
    activeVehicle: null,
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

vi.mock('@/composables/useSignalR', () => ({
  useSignalR: () => ({
    connected: ref(false),
    start: vi.fn<() => void>(),
    stop: vi.fn<() => void>(),
  }),
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
