import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { ref } from 'vue'
import { enableAutoUnmount, flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia, type Pinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import nl from '@/locales/nl.json'
import es from '@/locales/es.json'
import pt from '@/locales/pt.json'
import ptPT from '@/locales/pt-PT.json'
import { apiTokensApi } from '@/services/apiTokensApi'
import { dawarichApi, type DawarichStatus } from '@/services/dawarichApi'
import type { Vehicle } from '@/services/vehicleApi'
import { useVehicleStore } from '@/stores/vehicle'
import { useUiSettingsStore } from '@/stores/settingsUi'
import { useDashboardSettingsStore } from '@/stores/settingsDashboard'
import { useRdwStore } from '@/stores/rdw'
import SettingsModal from '../SettingsModal.vue'
import PhoneGpsSettings from '../PhoneGpsSettings.vue'
import { resetConnection } from '@/composables/useConnection'
import { setBrowserOnline } from '@/composables/__tests__/connectionStub'

vi.mock('@/services/apiTokensApi', async (original) => ({
  ...(await original<typeof import('@/services/apiTokensApi')>()),
  apiTokensApi: {
    list: vi.fn<typeof apiTokensApi.list>(),
    create: vi.fn<typeof apiTokensApi.create>(),
    revoke: vi.fn<typeof apiTokensApi.revoke>(),
    reference: vi.fn<typeof apiTokensApi.reference>(),
  },
}))
vi.mock('@/services/dawarichApi', () => ({
  dawarichApi: {
    status: vi.fn<typeof dawarichApi.status>(),
    trackers: vi.fn<typeof dawarichApi.trackers>(),
    save: vi.fn<typeof dawarichApi.save>(),
    backfill: vi.fn<typeof dawarichApi.backfill>(),
  },
}))
vi.mock('@/composables/useNotificationPushSync', () => ({
  useNotificationPushSync: () => ({
    pushSupported: false,
    pushEnabled: ref(false),
    pushSwitchDisabled: ref(false),
    showPermissionDeniedNotice: ref(false),
    showSubscribeFailedNotice: ref(false),
  }),
}))

const car: Vehicle = {
  id: 1,
  vin: 'FAKEVN00000000001',
  model: 'Test car',
  series: null,
  createdAt: '2026-01-01T00:00:00Z',
  vehicleType: 'hev',
  vehicleTypeOverride: null,
  hvBatteryCapacityKwh: null,
  supportsTargetSoc: false,
  hasPowerTailgate: false,
}
let pinia: Pinia
const scrollIntoView = vi.fn<(options?: boolean | ScrollIntoViewOptions) => void>()
const originalScroll = Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'scrollIntoView')
beforeAll(() => {
  Object.defineProperty(HTMLElement.prototype, 'scrollIntoView', {
    configurable: true,
    value: scrollIntoView,
  })
})
afterAll(() => {
  if (originalScroll) Object.defineProperty(HTMLElement.prototype, 'scrollIntoView', originalScroll)
  else Reflect.deleteProperty(HTMLElement.prototype, 'scrollIntoView')
})
enableAutoUnmount(afterEach)
afterEach(() => vi.restoreAllMocks())

beforeEach(() => {
  localStorage.clear()
  vi.resetAllMocks()
  pinia = createPinia()
  setActivePinia(pinia)
  useVehicleStore(pinia).vehicles = [{ ...car }]
  const rdw = useRdwStore(pinia)
  rdw.available = false
  vi.spyOn(rdw, 'ensureLoaded').mockResolvedValue()
  vi.mocked(apiTokensApi.list).mockResolvedValue([])
  vi.mocked(apiTokensApi.reference).mockResolvedValue({ enabled: false })
  vi.mocked(dawarichApi.status).mockResolvedValue({ configured: false, status: 'disabled' })
  vi.mocked(dawarichApi.trackers).mockResolvedValue({
    available: [{ trackerId: 'test-phone', lastSeenAt: '2026-01-01T00:00:00Z' }],
    linked: [{ trackerId: 'test-phone', createdAt: '2026-01-01T00:00:00Z' }],
    error: null,
  })
})

async function render(open = true) {
  const i18n = createI18n({
    legacy: false,
    locale: 'en',
    messages: { en, nl, es, pt, 'pt-PT': ptPT },
  })
  const wrapper = mount(SettingsModal, {
    props: { open },
    attachTo: document.body,
    global: { plugins: [pinia, i18n], stubs: { Teleport: true, FontAwesomeIcon: true } },
  })
  await flushPromises()
  return { wrapper, i18n }
}
type Wrapper = Awaited<ReturnType<typeof render>>['wrapper']
const tab = (wrapper: Wrapper, name: string) => wrapper.get(`[data-tab="${name}"]`)
const panel = (wrapper: Wrapper, name: string) => wrapper.get(`[id$="-panel-${name}"]`)

describe('SettingsModal tabs', () => {
  it('groups every setting and exposes one labelled panel at a time', async () => {
    const { wrapper } = await render()
    expect(wrapper.findAll('[role=tab]').map((button) => button.text())).toEqual([
      'General',
      'Vehicle',
      'Map',
      'Notifications',
      'Data & API',
    ])
    expect(panel(wrapper, 'general').isVisible()).toBe(true)
    wrapper.get('.detail-modal__body').element.scrollTop = 200
    for (const name of ['general', 'vehicle', 'map', 'notifications', 'data']) {
      await tab(wrapper, name).trigger('click')
      expect(wrapper.findAll('[role=tabpanel]').filter((item) => item.isVisible())).toHaveLength(1)
      expect(panel(wrapper, name).attributes('aria-labelledby')).toBe(
        tab(wrapper, name).attributes('id'),
      )
      expect(tab(wrapper, name).attributes('aria-controls')).toBe(
        panel(wrapper, name).attributes('id'),
      )
      expect(tab(wrapper, name).attributes('aria-selected')).toBe('true')
      expect(tab(wrapper, name).attributes('tabindex')).toBe('0')
      expect(wrapper.findAll('[role=tab][aria-selected=true]')).toHaveLength(1)
      expect(wrapper.get('.detail-modal__body').element.scrollTop).toBe(0)
    }
    expect(panel(wrapper, 'general').text()).toContain(en.settings.units.title)
    expect(panel(wrapper, 'vehicle').text()).toContain(en.settings.vehicleType.title)
    expect(panel(wrapper, 'vehicle').text()).toContain(en.settings.commutePlaces.title)
    expect(panel(wrapper, 'map').text()).toContain(en.settings.map.placeNames)
    expect(panel(wrapper, 'data').text()).toContain(en.settings.tripLogExport.download)
    const downloads = panel(wrapper, 'data')
      .findAll('a[download]')
      .map((link) => link.attributes('href'))
    expect(downloads.some((href) => href?.endsWith('/api/export'))).toBe(true)
    expect(downloads.some((href) => href?.endsWith('/api/diagnostics'))).toBe(true)
    expect(panel(wrapper, 'data').text()).toContain(en.settings.diagnostics.download)
  })

  it('wraps keyboard navigation and skips unavailable tabs', async () => {
    const { wrapper } = await render()
    await tab(wrapper, 'general').trigger('keydown', { key: 'ArrowLeft' })
    expect(document.activeElement).toBe(tab(wrapper, 'data').element)
    await tab(wrapper, 'data').trigger('keydown', { key: 'ArrowRight' })
    expect(document.activeElement).toBe(tab(wrapper, 'general').element)
    await tab(wrapper, 'general').trigger('keydown', { key: 'End' })
    expect(document.activeElement).toBe(tab(wrapper, 'data').element)
    await tab(wrapper, 'data').trigger('keydown', { key: 'Home' })
    await tab(wrapper, 'general').trigger('keydown', { key: 'ArrowRight' })
    await tab(wrapper, 'vehicle').trigger('keydown', { key: 'ArrowRight' })
    await tab(wrapper, 'map').trigger('keydown', { key: 'ArrowRight' })
    expect(document.activeElement).toBe(tab(wrapper, 'notifications').element)
    expect(scrollIntoView).toHaveBeenCalledWith({ block: 'nearest', inline: 'nearest' })
  })

  it('hides the complete phone tab when unconfigured or no vehicle is selected', async () => {
    const { wrapper } = await render()
    expect(wrapper.find('[data-tab=phone]').exists()).toBe(false)
    expect(panel(wrapper, 'phone').isVisible()).toBe(false)
    expect(dawarichApi.trackers).not.toHaveBeenCalled()
    useVehicleStore(pinia).vehicles = []
    await flushPromises()
    expect(wrapper.findComponent(PhoneGpsSettings).exists()).toBe(false)
  })

  it('discovers the phone tab without visiting it and preserves drafts across tabs', async () => {
    vi.mocked(dawarichApi.status).mockResolvedValue({ configured: true, status: 'ok' })
    const { wrapper } = await render()
    expect(tab(wrapper, 'phone').text()).toBe(en.settings.tabs.phoneShort)
    expect(tab(wrapper, 'phone').attributes('aria-label')).toBe(en.settings.tabs.phone)
    expect(tab(wrapper, 'phone').attributes('title')).toBe(en.settings.tabs.phone)
    await tab(wrapper, 'phone').trigger('click')
    await panel(wrapper, 'phone').get('input[type=date]').setValue('2026-01-02')
    const phone = wrapper.getComponent(PhoneGpsSettings)
    phone.getComponent({ name: 'Multiselect' }).vm.$emit('update:modelValue', [])
    await tab(wrapper, 'general').trigger('click')
    await tab(wrapper, 'phone').trigger('click')
    expect(panel(wrapper, 'phone').get<HTMLInputElement>('input[type=date]').element.value).toBe(
      '2026-01-02',
    )
    expect(phone.getComponent({ name: 'Multiselect' }).props('modelValue')).toEqual([])
    expect(dawarichApi.status).toHaveBeenCalledOnce()
  })

  it('keeps connection errors accessible, then removes an empty active tab and restores focus', async () => {
    vi.mocked(dawarichApi.status).mockRejectedValueOnce(new Error('offline'))
    const { wrapper } = await render()
    await tab(wrapper, 'phone').trigger('click')
    expect(panel(wrapper, 'phone').find('[role=alert]').exists()).toBe(true)
    const refresh = panel(wrapper, 'phone').get('button')
    let resolve!: (value: DawarichStatus) => void
    vi.mocked(dawarichApi.status).mockImplementationOnce(
      () =>
        new Promise((done) => {
          resolve = done
        }),
    )
    refresh.element.focus()
    await refresh.trigger('click')
    await flushPromises()
    expect(tab(wrapper, 'phone').attributes('aria-selected')).toBe('true')
    expect(refresh.attributes('disabled')).toBeDefined()
    refresh.element.blur()
    resolve({ configured: false, status: 'disabled' })
    await flushPromises()
    expect(wrapper.find('[data-tab=phone]').exists()).toBe(false)
    expect(panel(wrapper, 'general').isVisible()).toBe(true)
    expect(document.activeElement).toBe(tab(wrapper, 'general').element)
  })

  it('does not let a previous vehicle response reveal an inapplicable tab', async () => {
    let resolve!: (value: DawarichStatus) => void
    vi.mocked(dawarichApi.status).mockImplementationOnce(
      () =>
        new Promise((done) => {
          resolve = done
        }),
    )
    const { wrapper } = await render()
    useVehicleStore(pinia).vehicles = [{ ...car, id: 2, vin: 'FAKEVN00000000002' }]
    await flushPromises()
    resolve({ configured: true, status: 'ok' })
    await flushPromises()
    expect(wrapper.find('[data-tab=phone]').exists()).toBe(false)
  })

  it.each(['hev', 'phev', 'bev', 'unknown'] as const)(
    'preserves drivetrain and deployment visibility for %s',
    async (vehicleType) => {
      useVehicleStore(pinia).vehicles = [{ ...car, vehicleType }]
      const { wrapper } = await render()
      await tab(wrapper, 'notifications').trigger('click')
      const labels = () => panel(wrapper, 'notifications').text()
      expect(labels().includes(en.notifications.categories.chargingComplete)).toBe(
        vehicleType !== 'hev',
      )
      expect(labels().includes(en.notifications.categories.lowFuel)).toBe(vehicleType !== 'bev')
      expect(labels()).not.toContain(en.notifications.categories.rdw)
      expect(wrapper.find('#settings-toggle-push').exists()).toBe(false)
      useRdwStore(pinia).available = true
      await flushPromises()
      expect(labels()).toContain(en.notifications.categories.rdw)
      await tab(wrapper, 'vehicle').trigger('click')
      expect(panel(wrapper, 'vehicle').find('.license-plate-form').exists()).toBe(true)
      useRdwStore(pinia).available = false
      await flushPromises()
      expect(panel(wrapper, 'vehicle').find('.license-plate-form').exists()).toBe(false)
      expect(tab(wrapper, 'vehicle').text()).toBe(en.settings.tabs.vehicle)
    },
  )

  it('updates conditional notifications after a drivetrain override without losing exclusions', async () => {
    const { wrapper } = await render()
    const settings = useUiSettingsStore(pinia)
    settings.notificationTypeExclusions = ['low-fuel', 'charging-complete']
    settings.vehicleTypeOverride = 'bev'
    await tab(wrapper, 'notifications').trigger('click')
    expect(panel(wrapper, 'notifications').text()).not.toContain(
      en.notifications.categories.lowFuel,
    )
    expect(panel(wrapper, 'notifications').text()).toContain(
      en.notifications.categories.chargingComplete,
    )
    settings.vehicleTypeOverride = 'phev'
    await flushPromises()
    expect(panel(wrapper, 'notifications').text()).toContain(en.notifications.categories.lowFuel)
    expect(settings.notificationTypeExclusions).toEqual(['low-fuel', 'charging-complete'])
  })

  it('preserves a new one-time token and unsaved name while switching tabs', async () => {
    vi.mocked(apiTokensApi.create).mockResolvedValue({
      token: 'synthetic-test-token',
      details: {
        id: 1,
        name: 'Test',
        prefix: 'test',
        createdAt: '2026-01-01T00:00:00Z',
        lastUsedAt: null,
      },
    })
    const { wrapper } = await render()
    await tab(wrapper, 'data').trigger('click')
    await panel(wrapper, 'data').get('form input').setValue('Test')
    await panel(wrapper, 'data').get('form').trigger('submit')
    await flushPromises()
    await panel(wrapper, 'data').get('form input').setValue('Next draft')
    await tab(wrapper, 'general').trigger('click')
    await tab(wrapper, 'data').trigger('click')
    expect(panel(wrapper, 'data').get<HTMLInputElement>('input[readonly]').element.value).toBe(
      'synthetic-test-token',
    )
    expect(panel(wrapper, 'data').get<HTMLInputElement>('form input').element.value).toBe(
      'Next draft',
    )
    expect(apiTokensApi.list).toHaveBeenCalledOnce()
  })

  it('localizes tab names and resets to General when reopened', async () => {
    vi.mocked(dawarichApi.status).mockResolvedValue({ configured: true, status: 'ok' })
    const { wrapper, i18n } = await render()
    await tab(wrapper, 'data').trigger('click')
    for (const locale of ['nl', 'es', 'pt', 'pt-PT'] as const) {
      i18n.global.locale.value = locale
      await flushPromises()
      expect(tab(wrapper, 'vehicle').text()).toBe(
        { nl, es, pt, 'pt-PT': ptPT }[locale].settings.tabs.vehicle,
      )
      expect(tab(wrapper, 'phone').text()).toBe(
        { nl, es, pt, 'pt-PT': ptPT }[locale].settings.tabs.phoneShort,
      )
      expect(tab(wrapper, 'phone').attributes('aria-label')).toBe(
        { nl, es, pt, 'pt-PT': ptPT }[locale].settings.tabs.phone,
      )
      expect(tab(wrapper, 'phone').attributes('title')).toBe(
        { nl, es, pt, 'pt-PT': ptPT }[locale].settings.tabs.phone,
      )
      expect(wrapper.get('[role=dialog]').attributes('aria-label')).toBe(
        { nl, es, pt, 'pt-PT': ptPT }[locale].settings.title,
      )
    }
    await wrapper.setProps({ open: false })
    await wrapper.setProps({ open: true })
    await flushPromises()
    expect(tab(wrapper, 'general').attributes('aria-selected')).toBe('true')
  })

  it.each([en, nl, es, pt, ptPT])(
    'keeps navigation hints consistent with the settings labels',
    (locale) => {
      const { title, tabs, licensePlate, commutePlaces } = locale.settings
      expect(locale.tripLog.placesOff).toContain(`${title} > ${tabs.map}`)
      expect(locale.tripLog.subtitle).toContain(`${title} > ${tabs.data}`)
      expect(locale.tripLog.cardDesc.commute).toContain(
        `${title} > ${tabs.vehicle} > ${commutePlaces.title}`,
      )
      expect(locale.tripLog.places.notifyHint).toContain(`${title} > ${tabs.notifications}`)
      expect(locale.rdw.notFound).toContain(`${title} > ${tabs.vehicle} > ${licensePlate.title}`)
    },
  )
})

describe('SettingsModal without the server', () => {
  beforeEach(() => setBrowserOnline(false))
  afterEach(() => resetConnection())

  it("offers only this browser's own settings, which reach the account later", async () => {
    useRdwStore(pinia).available = true
    const { wrapper } = await render()

    expect(wrapper.findAll('[role=tab]').map((button) => button.text())).toEqual([
      'General',
      'Vehicle',
      'Map',
      'Notifications',
    ])
    expect(panel(wrapper, 'vehicle').find('.license-plate-form').exists()).toBe(false)
  })
})

describe('SettingsModal appearance', () => {
  it('switches the map behind the dashboard on and back off, for the old layout', async () => {
    const { wrapper } = await render()
    const settings = useDashboardSettingsStore(pinia)
    const toggle = panel(wrapper, 'general').get('#footer-toggle-map-backdrop')
    expect(panel(wrapper, 'general').text()).toContain(en.settings.dashboard.mapBackdrop)
    expect((toggle.element as HTMLInputElement).checked).toBe(false)

    await toggle.setValue(true)
    expect(settings.mapBackdrop).toBe(true)

    await toggle.setValue(false)
    expect(settings.mapBackdrop).toBe(false)
  })
  describe('this device', () => {
    afterEach(() => {
      Reflect.deleteProperty(navigator, 'wakeLock')
      Reflect.deleteProperty(navigator, 'geolocation')
      vi.unstubAllGlobals()
    })

    function runAs(installed: boolean) {
      vi.stubGlobal('matchMedia', (query: string) => ({
        matches: installed && query === '(display-mode: standalone)',
        addEventListener: () => {},
        removeEventListener: () => {},
      }))
      Object.defineProperty(navigator, 'wakeLock', {
        configurable: true,
        value: { request: vi.fn<() => Promise<never>>() },
      })
      Object.defineProperty(navigator, 'geolocation', {
        configurable: true,
        value: { watchPosition: vi.fn<() => number>(), clearWatch: vi.fn<() => void>() },
      })
    }

    it('is offered in the installed app and switches the setting', async () => {
      runAs(true)
      const { wrapper } = await render()
      const toggle = panel(wrapper, 'general').get('#footer-toggle-keep-screen-on')
      expect(panel(wrapper, 'general').text()).toContain(en.settings.keepScreenOn.label)

      await toggle.setValue(true)
      expect(useUiSettingsStore(pinia).keepScreenOn).toBe(true)
    })

    it('is not offered in a browser tab', async () => {
      runAs(false)
      const { wrapper } = await render()
      expect(panel(wrapper, 'general').find('#footer-toggle-keep-screen-on').exists()).toBe(false)
      expect(panel(wrapper, 'general').find('#footer-toggle-record-phone-gps').exists()).toBe(false)
    })

    it('offers recording GPS only once the screen is kept on', async () => {
      runAs(true)
      const { wrapper } = await render()
      const general = panel(wrapper, 'general')
      const gps = general.get<HTMLInputElement>('#footer-toggle-record-phone-gps')
      expect(gps.element.disabled).toBe(true)
      expect(general.text()).toContain(en.settings.phoneGps.needsScreen)

      await general.get('#footer-toggle-keep-screen-on').setValue(true)
      expect(gps.element.disabled).toBe(false)
      await gps.setValue(true)

      expect(useUiSettingsStore(pinia).recordPhoneGps).toBe(true)
      expect(general.text()).not.toContain(en.settings.phoneGps.needsScreen)
    })
  })
})
