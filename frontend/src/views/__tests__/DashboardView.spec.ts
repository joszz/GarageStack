import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { enableAutoUnmount, flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia, type Pinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import DashboardView from '../DashboardView.vue'
import { vehicleApi, type TelemetrySnapshot, type Trip, type Vehicle } from '@/services/vehicleApi'
import { rdwApi } from '@/services/rdwApi'
import { ApiError } from '@/services/apiCore'
import { useVehicleStore } from '@/stores/vehicle'

vi.mock('@/services/vehicleApi', () => ({
  vehicleApi: {
    list: vi.fn<() => Promise<Vehicle[]>>(),
    status: vi.fn<() => Promise<TelemetrySnapshot | null>>(),
    config: vi.fn<() => Promise<Record<string, string>>>(),
    latestTrip: vi.fn<() => Promise<Trip | undefined>>(),
  },
}))

// Loaded only in edit mode, as an async component, which a stub does not reach.
// __esModule, so defineAsyncComponent takes the default export as the component.
vi.mock('@/components/EditableCardGrid.vue', () => ({
  __esModule: true,
  default: { template: '<div class="card-grid" />' },
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
    Reflect.deleteProperty(navigator, 'serviceWorker')
  })

  it('loads everything the cards show for the car', async () => {
    await mountView()

    for (const call of [vehicleApi.status, vehicleApi.config, vehicleApi.latestTrip, rdwApi.get])
      expect(call).toHaveBeenCalledWith('FAKEVN00000000001')
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

  it('offers to reset the layout while it is being edited', async () => {
    const wrapper = await mountView()

    await wrapper
      .findAll('button')
      .find((b) => b.text() === en.dashboard.editLayout)!
      .trigger('click')
    await vi.dynamicImportSettled()
    await flushPromises()

    expect(wrapper.find('.card-grid').exists()).toBe(true)
    expect(wrapper.text()).toContain(en.dashboard.resetLayout)
    expect(wrapper.text()).toContain(en.dashboard.doneEditing)
  })
})
