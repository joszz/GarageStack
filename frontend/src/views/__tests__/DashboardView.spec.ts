import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { enableAutoUnmount, flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia, type Pinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import DashboardView from '../DashboardView.vue'
import DashboardMapBackdrop from '@/components/DashboardMapBackdrop.vue'
import { vehicleApi, type TelemetrySnapshot, type Trip, type Vehicle } from '@/services/vehicleApi'
import { rdwApi } from '@/services/rdwApi'
import { ApiError } from '@/services/apiCore'
import { useVehicleStore } from '@/stores/vehicle'
import { useDashboardSettingsStore } from '@/stores/settingsDashboard'
import { useUiSettingsStore } from '@/stores/settingsUi'
import { LONG_PRESS_MS } from '@/composables/useLongPress'
import { rememberVin } from '@/utils/lastVin'

vi.mock('@/services/vehicleApi', () => ({
  vehicleApi: {
    list: vi.fn<() => Promise<Vehicle[]>>(),
    status: vi.fn<(vin: string) => Promise<TelemetrySnapshot | null>>(),
    config: vi.fn<() => Promise<Record<string, string>>>(),
    latestTrip: vi.fn<() => Promise<Trip | undefined>>(),
  },
}))

// Loaded only as edit mode opens (useEditMode), which a stub does not reach. It hands each card to
// the slot, as the real grid does, without the dragging.
vi.mock('@/components/EditableCardGrid.vue', () => ({
  __esModule: true,
  default: {
    props: ['modelValue'],
    template:
      '<div class="card-grid"><div v-for="item in modelValue" :key="item.id"><slot :item="item" /><slot name="controls" :item="item" /></div></div>',
  },
}))

vi.mock('@/services/rdwApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/services/rdwApi')>()),
  rdwApi: { get: vi.fn<(typeof import('@/services/rdwApi'))['rdwApi']['get']>() },
}))

// A car that has not reported yet: the API answers 204, which the client returns as undefined.
const noStatus = undefined as unknown as TelemetrySnapshot

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

// A dashboard left mounted keeps listening for the tab coming back into view.
enableAutoUnmount(afterEach)

function car(vehicleType: Vehicle['vehicleType']): Vehicle {
  return { id: 1, vin: 'FAKEVN00000000001', vehicleType } as Vehicle
}

// What a parked electric car reports; fuel fields are null, as the API sends them.
const parked = {
  recordedAt: '2026-10-19T10:00:00Z',
  evSocPercent: 71,
  fuelLevelPercent: null,
  fuelRangeKm: null,
  odometerKm: 24852,
  batteryVoltage: 12.7,
  isLocked: true,
} as unknown as TelemetrySnapshot

async function mountView() {
  const wrapper = mount(DashboardView, {
    global: {
      plugins: [i18n, pinia],
      stubs: {
        FontAwesomeIcon: true,
        CarDiagram: true,
        LocationMapWidget: true,
        DashboardMapBackdrop: true,
        DashboardParkingSpot: true,
        CardInfoWrap: { template: '<div><slot /></div>' },
        DashboardCardContent: {
          props: ['cardId'],
          template: '<div class="card" :data-id="cardId" />',
        },
      },
    },
  })
  await flushPromises()
  return wrapper
}

const cardIds = (wrapper: Awaited<ReturnType<typeof mountView>>) =>
  wrapper.findAll('.card').map((c) => c.attributes('data-id'))

// Handed to the mount and to every store the test reads, rather than left to the active pinia,
// which a busy test run has been seen to switch between a mount and its assertions.
let pinia: Pinia

describe('DashboardView', () => {
  beforeEach(() => {
    localStorage.clear()
    pinia = createPinia()
    setActivePinia(pinia)
    vi.clearAllMocks()
    vi.mocked(vehicleApi.list).mockResolvedValue([car('bev')])
    vi.mocked(vehicleApi.status).mockResolvedValue(parked)
    vi.mocked(vehicleApi.config).mockResolvedValue({})
    vi.mocked(vehicleApi.latestTrip).mockResolvedValue(undefined)
    vi.mocked(rdwApi.get).mockRejectedValue(new ApiError(404, '/rdw', { code: 'rdw.disabled' }))
  })

  afterEach(() => {
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
    Reflect.deleteProperty(navigator, 'serviceWorker')
  })

  // What a parked car's readings mean, where the history is, and how to rearrange the cards.
  it('explains the page behind its info button', async () => {
    const wrapper = await mountView()

    expect(wrapper.findComponent({ name: 'ViewHeader' }).props('subtitle')).toBe(
      en.dashboard.subtitle,
    )
  })

  it('loads everything the cards show for the car', async () => {
    await mountView()

    for (const call of [vehicleApi.status, vehicleApi.config, vehicleApi.latestTrip, rdwApi.get])
      expect(call).toHaveBeenCalledWith('FAKEVN00000000001')
  })

  it('asks for the car this browser saw last alongside the vehicle list, not after it', async () => {
    rememberVin('FAKEVN00000000001')
    let answerList: (vehicles: Vehicle[]) => void = () => {}
    vi.mocked(vehicleApi.list).mockReturnValue(new Promise((resolve) => (answerList = resolve)))

    const mounted = mountView()
    await Promise.resolve()
    expect(vehicleApi.status).toHaveBeenCalledExactlyOnceWith('FAKEVN00000000001')

    answerList([car('bev')])
    await mounted
    expect(vehicleApi.latestTrip).toHaveBeenCalledOnce()
  })

  it('asks for the listed car after all when the list names another one', async () => {
    rememberVin('FAKEVN00000000002')
    vi.mocked(vehicleApi.status).mockImplementation(async (vin) =>
      vin === 'FAKEVN00000000001' ? parked : { ...parked, evSocPercent: 12 },
    )

    await mountView()

    expect(vehicleApi.status).toHaveBeenLastCalledWith('FAKEVN00000000001')
    expect(useVehicleStore(pinia).currentStatus?.evSocPercent).toBe(71)
  })

  it("shows the cards the car's drivetrain has, and leaves out the ones it has no use for", async () => {
    const wrapper = await mountView()

    const shown = cardIds(wrapper)
    expect(shown).toEqual(expect.arrayContaining(['evBattery', 'odometer', 'battery12v', 'doors']))
    expect(shown).not.toContain('fuelLevel')
    expect(shown).not.toContain('rdw')
  })

  it('says there is no data yet when the car has reported nothing', async () => {
    vi.mocked(vehicleApi.status).mockResolvedValue(noStatus)

    const wrapper = await mountView()

    expect(wrapper.text()).toContain(en.dashboard.noData)
    expect(cardIds(wrapper)).toEqual([])
  })

  it('says something went wrong when the status cannot be loaded', async () => {
    vi.mocked(vehicleApi.status).mockRejectedValue(new ApiError(500, '/status'))

    const wrapper = await mountView()

    expect(wrapper.find('.error-state').text()).toContain(en.common.error)
  })

  it('reads the live status and the newest trip again when the tab comes back into view', async () => {
    await mountView()
    vi.clearAllMocks()

    vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('visible')
    document.dispatchEvent(new Event('visibilitychange'))
    await flushPromises()

    expect(vehicleApi.status).toHaveBeenCalledOnce()
    expect(vehicleApi.latestTrip).toHaveBeenCalledOnce()
    expect(vehicleApi.config).not.toHaveBeenCalled()
  })

  it('reads the newest trip again once a trip ends', async () => {
    await mountView()
    vi.mocked(vehicleApi.latestTrip).mockClear()

    useVehicleStore(pinia).notifyTripCompleted()
    await flushPromises()

    expect(vehicleApi.latestTrip).toHaveBeenCalledOnce()
  })

  it('reads the status again when a push notification arrives', async () => {
    const serviceWorker = new EventTarget()
    Object.defineProperty(navigator, 'serviceWorker', { value: serviceWorker, configurable: true })
    await mountView()
    vi.mocked(vehicleApi.status).mockClear()

    serviceWorker.dispatchEvent(
      new MessageEvent('message', { data: { type: 'NOTIFICATION_RECEIVED' } }),
    )
    await flushPromises()

    expect(vehicleApi.status).toHaveBeenCalledOnce()
  })

  it('offers to reset the layout beside Done while it is being edited', async () => {
    const wrapper = await mountView()

    await wrapper
      .findAll('button')
      .find((b) => b.text() === en.dashboard.editLayout)!
      .trigger('click')
    await vi.dynamicImportSettled()
    await flushPromises()

    expect(wrapper.find('.card-grid').exists()).toBe(true)
    expect(wrapper.text()).toContain(en.dashboard.resetLayoutShort)
    expect(wrapper.text()).toContain(en.dashboard.doneEditing)
  })

  it('starts editing the layout when a finger rests on a card, as on a phone', async () => {
    const wrapper = await mountView()

    vi.useFakeTimers()
    wrapper
      .find('.card')
      .element.dispatchEvent(
        new PointerEvent('pointerdown', { bubbles: true, isPrimary: true, pointerType: 'touch' }),
      )
    vi.advanceTimersByTime(LONG_PRESS_MS)
    vi.useRealTimers()
    await vi.dynamicImportSettled()
    await flushPromises()

    expect(wrapper.find('.card-grid').exists()).toBe(true)
    expect(wrapper.text()).toContain(en.dashboard.doneEditing)
  })

  it('shows the location map before the car diagram once the two are swapped', async () => {
    useDashboardSettingsStore(pinia).mapFirst = true
    const wrapper = await mountView()

    const row = wrapper.find('.overview-row')
    const order = row.findAll(':scope > *').map((el) => el.element.tagName.toLowerCase())
    expect(order).toEqual(['location-map-widget-stub', 'car-diagram-stub'])
  })

  it('lets the car diagram and the location map be dragged in a list of their own', async () => {
    const wrapper = await mountView()

    await wrapper
      .findAll('button')
      .find((b) => b.text() === en.dashboard.editLayout)!
      .trigger('click')
    await vi.dynamicImportSettled()
    await flushPromises()

    const overview = wrapper.find('.overview-row')
    expect(overview.classes()).toContain('card-grid')
    expect(overview.find('car-diagram-stub').exists()).toBe(true)
    expect(overview.find('location-map-widget-stub').exists()).toBe(true)
    // The other cards are in a list of their own, so neither can trade places with them.
    expect(overview.find('.card').exists()).toBe(false)
  })

  // Edit mode gave the row a top margin of its own, so every card dropped as editing began.
  it('keeps the overview row spaced the same while editing, so no card moves', async () => {
    const wrapper = await mountView()
    const spacing = () =>
      wrapper
        .get('.overview-row')
        .classes()
        .filter((name) => /^m[tb]-/.test(name))
        .sort()
    const before = spacing()

    await wrapper
      .findAll('button')
      .find((b) => b.text() === en.dashboard.editLayout)!
      .trigger('click')
    await vi.dynamicImportSettled()
    await flushPromises()

    expect(wrapper.get('.overview-row').classes()).toContain('card-grid')
    expect(spacing()).toEqual(before)
  })

  // Issue #570: a card the car sends nothing for showed as a placeholder while editing and then
  // vanished on Done, with nothing saying why.
  it('says while editing which shown cards stay hidden until the car reports them', async () => {
    vi.mocked(vehicleApi.status).mockResolvedValue({ ...parked, electricRangeKm: null })
    const wrapper = await mountView()

    await wrapper
      .findAll('button')
      .find((b) => b.text() === en.dashboard.editLayout)!
      .trigger('click')
    await vi.dynamicImportSettled()
    await flushPromises()

    const placeholder = (name: string) =>
      wrapper.findAll('.card-slot__placeholder').find((p) => p.text().startsWith(name))
    // Shown by default on a BEV, but the car sends no electric range.
    expect(placeholder(en.settings.cards.electricRange)!.text()).toContain(
      en.dashboard.cardWithoutData,
    )
    // Hidden by default: a placeholder because it is switched off, not for want of data.
    expect(placeholder(en.settings.cards.speed)!.text()).not.toContain(en.dashboard.cardWithoutData)
  })

  // A phone stands the overview's two cards one above the other and can trade height between
  // them; the page only says which, and dashboard.css does the rest.
  describe('the overview on a phone', () => {
    const sizeClasses = (wrapper: Awaited<ReturnType<typeof mountView>>) =>
      wrapper
        .get('.view-container')
        .classes()
        .filter((name) => /^dashboard--(car|map)-(small|smaller|smallest|tall|taller)$/.test(name))

    it('has no size class while both cards are standard, so they are as they always were', async () => {
      const wrapper = await mountView()

      expect(sizeClasses(wrapper)).toEqual([])
    })

    it.each(['small', 'smaller', 'smallest'] as const)(
      'shrinks the car diagram to %s',
      async (size) => {
        useUiSettingsStore(pinia).overviewCarSize = size
        const wrapper = await mountView()

        expect(sizeClasses(wrapper)).toEqual([`dashboard--car-${size}`])
      },
    )

    it.each(['tall', 'taller'] as const)('makes the location map %s', async (height) => {
      useUiSettingsStore(pinia).overviewMapHeight = height
      const wrapper = await mountView()

      expect(sizeClasses(wrapper)).toEqual([`dashboard--map-${height}`])
    })

    it('trades one for the other when both are chosen', async () => {
      const ui = useUiSettingsStore(pinia)
      ui.overviewCarSize = 'smaller'
      ui.overviewMapHeight = 'taller'
      const wrapper = await mountView()

      expect(sizeClasses(wrapper).sort()).toEqual([
        'dashboard--car-smaller',
        'dashboard--map-taller',
      ])
    })

    it('follows a change made while the page is open', async () => {
      const wrapper = await mountView()
      const ui = useUiSettingsStore(pinia)

      ui.overviewMapHeight = 'tall'
      await flushPromises()
      expect(sizeClasses(wrapper)).toEqual(['dashboard--map-tall'])

      ui.overviewMapHeight = 'normal'
      await flushPromises()
      expect(sizeClasses(wrapper)).toEqual([])
    })
  })

  // In edit mode a phone has magnifier buttons over the car diagram and the location map, which
  // step each through its sizes. The car's go to smaller, the map's to taller, and a button
  // that has nowhere further to go is dimmed.
  describe('the size buttons in edit mode', () => {
    function screenIs(phone: boolean) {
      vi.stubGlobal('matchMedia', (query: string) => ({
        matches: phone && query === '(width <= 767px)',
        media: query,
        addEventListener: () => {},
        removeEventListener: () => {},
      }))
    }

    // A phone puts the page's buttons in the top bar the layout provides, outside the view.
    afterEach(() => document.body.replaceChildren())

    async function edit(phone = true) {
      screenIs(phone)
      for (const id of ['mobile-topbar-title', 'mobile-topbar-page-actions']) {
        const target = document.createElement('div')
        target.id = id
        document.body.append(target)
      }
      const wrapper = await mountView()
      // In the top bar on a phone, in the page's own header beside the sidebar.
      const editButton = [
        ...document.querySelectorAll('button'),
        ...wrapper.element.querySelectorAll('button'),
      ].find((b) => b.textContent.trim() === en.dashboard.editLayout)
      editButton!.click()
      await vi.dynamicImportSettled()
      await flushPromises()
      return wrapper
    }

    // Both overview cards, in the order they stand: the car, then the map.
    const controlsOf = (wrapper: Awaited<ReturnType<typeof mountView>>, index: number) => {
      const group = wrapper.findAll('.card-slot__size')[index]!
      const [smaller, larger] = group.findAll('button')
      return { group, smaller: smaller!, larger: larger! }
    }

    it('are on each of the two overview cards of a phone', async () => {
      const wrapper = await edit()

      expect(wrapper.findAll('.card-slot__size')).toHaveLength(2)
      expect(controlsOf(wrapper, 0).group.attributes('aria-label')).toBe(en.vehicle.overview)
      expect(controlsOf(wrapper, 1).group.attributes('aria-label')).toBe(en.vehicle.location)
    })

    it('are not on a screen wider than a phone, where the cards stand side by side', async () => {
      const wrapper = await edit(false)

      expect(wrapper.find('.card-slot__size').exists()).toBe(false)
    })

    it('are not on a card that is hidden', async () => {
      useDashboardSettingsStore(pinia).overview[1]!.visible = false
      const wrapper = await edit()

      expect(wrapper.findAll('.card-slot__size')).toHaveLength(1)
    })

    it('start dimmed in the direction a card is already standard in', async () => {
      const wrapper = await edit()

      // The car is only ever made smaller, and the map only taller, from standard.
      expect(controlsOf(wrapper, 0).larger.attributes('aria-disabled')).toBe('true')
      expect(controlsOf(wrapper, 0).smaller.attributes('aria-disabled')).toBe('false')
      expect(controlsOf(wrapper, 1).smaller.attributes('aria-disabled')).toBe('true')
      expect(controlsOf(wrapper, 1).larger.attributes('aria-disabled')).toBe('false')
    })

    it('step the car diagram down through its sizes and back', async () => {
      const ui = useUiSettingsStore(pinia)
      const wrapper = await edit()

      await controlsOf(wrapper, 0).smaller.trigger('click')
      expect(ui.overviewCarSize).toBe('small')
      await controlsOf(wrapper, 0).smaller.trigger('click')
      expect(ui.overviewCarSize).toBe('smaller')
      await controlsOf(wrapper, 0).smaller.trigger('click')
      expect(ui.overviewCarSize).toBe('smallest')
      expect(controlsOf(wrapper, 0).smaller.attributes('aria-disabled')).toBe('true')

      await controlsOf(wrapper, 0).larger.trigger('click')
      await controlsOf(wrapper, 0).larger.trigger('click')
      expect(ui.overviewCarSize).toBe('small')
      await controlsOf(wrapper, 0).larger.trigger('click')
      expect(ui.overviewCarSize).toBe('normal')
    })

    it('step the location map up through its heights and back', async () => {
      const ui = useUiSettingsStore(pinia)
      const wrapper = await edit()

      await controlsOf(wrapper, 1).larger.trigger('click')
      expect(ui.overviewMapHeight).toBe('tall')
      await controlsOf(wrapper, 1).larger.trigger('click')
      expect(ui.overviewMapHeight).toBe('taller')
      expect(controlsOf(wrapper, 1).larger.attributes('aria-disabled')).toBe('true')

      await controlsOf(wrapper, 1).smaller.trigger('click')
      expect(ui.overviewMapHeight).toBe('tall')
      await controlsOf(wrapper, 1).smaller.trigger('click')
      expect(ui.overviewMapHeight).toBe('normal')
    })

    it('show the result at once, in the layout being edited', async () => {
      const wrapper = await edit()

      await controlsOf(wrapper, 0).smaller.trigger('click')
      await controlsOf(wrapper, 1).larger.trigger('click')

      expect(wrapper.get('.view-container').classes()).toEqual(
        expect.arrayContaining(['dashboard--car-small', 'dashboard--map-tall']),
      )
    })
  })

  describe('map behind the dashboard', () => {
    // A screen beside the sidebar, or a phone.
    function screenIsWide(wide: boolean) {
      vi.stubGlobal('matchMedia', (query: string) => ({
        matches: wide && query === '(width >= 768px)',
        media: query,
        addEventListener: () => {},
        removeEventListener: () => {},
      }))
    }

    it('lays the map under the cards and parks the car where the location card was', async () => {
      screenIsWide(true)
      useDashboardSettingsStore(pinia).mapBackdrop = true
      const wrapper = await mountView()

      expect(wrapper.get('.view-container').classes()).toContain('dashboard--map-backdrop')
      expect(wrapper.find('location-map-widget-stub').exists()).toBe(false)
      const spot = wrapper.get('dashboard-parking-spot-stub')
      expect(wrapper.getComponent(DashboardMapBackdrop).props('spot')).toBe(spot.element)
    })

    it('keeps the parking spot, and the cards with the car diagram, where they stood once swapped', async () => {
      screenIsWide(true)
      const settings = useDashboardSettingsStore(pinia)
      settings.mapBackdrop = true
      settings.mapFirst = true
      const wrapper = await mountView()

      const order = wrapper
        .get('.overview-row')
        .findAll(':scope > *')
        .map((el) => el.element.tagName.toLowerCase())
      expect(order).toEqual(['dashboard-parking-spot-stub', 'car-diagram-stub'])
      // So the cards below keep to the car diagram's side on a wide screen (dashboard.css).
      expect(wrapper.get('.view-container').classes()).toContain('dashboard--map-first')
    })

    it('keeps the location card on a phone, where the cards would cover the map', async () => {
      screenIsWide(false)
      useDashboardSettingsStore(pinia).mapBackdrop = true
      const wrapper = await mountView()

      expect(wrapper.find('location-map-widget-stub').exists()).toBe(true)
      expect(wrapper.findComponent(DashboardMapBackdrop).exists()).toBe(false)
      expect(wrapper.get('.view-container').classes()).not.toContain('dashboard--map-backdrop')
    })

    it('has no map behind while the location map is hidden', async () => {
      screenIsWide(true)
      const settings = useDashboardSettingsStore(pinia)
      settings.mapBackdrop = true
      settings.showLocationMap = false
      const wrapper = await mountView()

      expect(wrapper.findComponent(DashboardMapBackdrop).exists()).toBe(false)
      expect(wrapper.find('dashboard-parking-spot-stub').exists()).toBe(false)
    })

    it('keeps the car in the placeholder that stands in for the spot while editing', async () => {
      screenIsWide(true)
      useDashboardSettingsStore(pinia).mapBackdrop = true
      const wrapper = await mountView()

      await wrapper
        .findAll('button')
        .find((b) => b.text() === en.dashboard.editLayout)!
        .trigger('click')
      await vi.dynamicImportSettled()
      await flushPromises()

      const placeholder = wrapper.get('.parking-spot--edit')
      expect(placeholder.text()).toBe(en.vehicle.location)
      expect(wrapper.find('location-map-widget-stub').exists()).toBe(false)
      expect(wrapper.getComponent(DashboardMapBackdrop).props('spot')).toBe(placeholder.element)
    })
  })
})
