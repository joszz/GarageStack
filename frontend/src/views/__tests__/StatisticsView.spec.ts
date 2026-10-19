import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { defineComponent, h } from 'vue'
import { enableAutoUnmount, flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia, type Pinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import StatisticsView from '../StatisticsView.vue'
import {
  vehicleApi,
  type DailyStat,
  type TelemetrySnapshot,
  type TripSummary,
  type Vehicle,
  type VehicleAggregateStats,
} from '@/services/vehicleApi'
import { ApiError } from '@/services/apiCore'
import { useVehicleStore } from '@/stores/vehicle'
import { useUiSettingsStore } from '@/stores/settingsUi'

vi.mock('@/services/vehicleApi', () => ({
  vehicleApi: {
    list: vi.fn<() => Promise<Vehicle[]>>(),
    status: vi.fn<() => Promise<TelemetrySnapshot | null>>(),
    config: vi.fn<() => Promise<Record<string, string>>>(),
    dailyStats: vi.fn<() => Promise<DailyStat[]>>(),
    tripSummaries: vi.fn<() => Promise<TripSummary[]>>(),
    stats: vi.fn<() => Promise<VehicleAggregateStats>>(),
  },
}))

// The parking map is Leaflet's business; a marker per spot is all these tests look at.
vi.mock('@vue-leaflet/vue-leaflet', () => ({
  LMap: defineComponent({
    setup:
      (_, { slots }) =>
      () =>
        h('div', slots.default?.()),
  }),
  LMarker: defineComponent({ setup: () => () => h('div', { class: 'marker' }) }),
}))
vi.mock('@/composables/useBasemap', () => ({ useBasemap: () => undefined }))

// Loaded only in edit mode, as an async component; see DashboardView.spec.ts.
vi.mock('@/components/EditableCardGrid.vue', () => ({
  __esModule: true,
  default: { template: '<div class="card-grid" />' },
}))

// A car that has not reported yet: the API answers 204, which the client returns as undefined.
const noStatus = undefined as unknown as TelemetrySnapshot

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })
const insights = en.statistics.insights

enableAutoUnmount(afterEach)

function car(vehicleType: Vehicle['vehicleType']): Vehicle {
  return { id: 1, vin: 'FAKEVN00000000001', vehicleType } as Vehicle
}

function trip(overrides: Partial<TripSummary>): TripSummary {
  return {
    index: 0,
    id: 1,
    startedAt: '2026-10-10T07:00:00Z',
    endedAt: '2026-10-10T07:30:00Z',
    distanceKm: 10,
    pointCount: 50,
    endLatitude: 52.09,
    endLongitude: 5.12,
    maxSpeedKmh: null,
    avgMovingSpeedKmh: null,
    movingSpeedSamples: 0,
    ...overrides,
  }
}

// Three trips, two of them ending at the same place.
const trips = [
  trip({ index: 0, id: 1, distanceKm: 12 }),
  trip({ index: 1, id: 2, distanceKm: 30, endLatitude: 52.37, endLongitude: 4.9 }),
  trip({ index: 2, id: 3, distanceKm: 18 }),
]

const parked = { recordedAt: '2026-10-19T10:00:00Z', batteryVoltage: 12.6 } as TelemetrySnapshot

async function mountView() {
  const wrapper = mount(StatisticsView, {
    global: {
      plugins: [i18n, pinia],
      stubs: {
        FontAwesomeIcon: true,
        StatsPeriodFilter: true,
        StatsChartCard: true,
        StatsMonthTable: { template: '<div class="month-table" />' },
        CardInfoWrap: { template: '<div><slot /></div>' },
      },
    },
  })
  await flushPromises()
  return wrapper
}

// Each insight card shown, by its label, with its value and unit as the card's tooltip has them.
function shownInsights(wrapper: Awaited<ReturnType<typeof mountView>>) {
  return Object.fromEntries(
    wrapper
      .findAll('.status-card')
      .map((card) => [
        card.find('.status-card__label').text(),
        card.find('.status-card__value').attributes('title'),
      ]),
  )
}

// Handed to the mount and to every store the test reads, rather than left to the active pinia,
// which a busy test run has been seen to switch between a mount and its assertions.
let pinia: Pinia

describe('StatisticsView', () => {
  beforeEach(() => {
    localStorage.clear()
    pinia = createPinia()
    setActivePinia(pinia)
    vi.clearAllMocks()
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(new Date('2026-10-19T12:00:00Z'))
    vi.mocked(vehicleApi.list).mockResolvedValue([car('hev')])
    vi.mocked(vehicleApi.status).mockResolvedValue(parked)
    vi.mocked(vehicleApi.config).mockResolvedValue({})
    vi.mocked(vehicleApi.dailyStats).mockResolvedValue([])
    vi.mocked(vehicleApi.tripSummaries).mockResolvedValue(trips)
    vi.mocked(vehicleApi.stats).mockResolvedValue({
      climateUsagePct: 25,
      climateOnSnapshots: 1,
      totalClimateSnapshots: 4,
    })
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  // The last 30 days are the 30 before today, and today.
  it('loads the last 30 days for the car', async () => {
    await mountView()

    const [vin, from, to] = vi.mocked(vehicleApi.dailyStats).mock.calls[0]!
    expect(vin).toBe('FAKEVN00000000001')
    expect(from).toBe('2026-09-19')
    expect(to).toBe('2026-10-19')
    const [, fromIso, toIso] = vi.mocked(vehicleApi.tripSummaries).mock.calls[0]!
    expect(vehicleApi.stats).toHaveBeenCalledWith('FAKEVN00000000001', fromIso, toIso)
    expect(vehicleApi.status).toHaveBeenCalledWith('FAKEVN00000000001')
    expect(vehicleApi.config).toHaveBeenCalledWith('FAKEVN00000000001')
  })

  it("sums the period's trips into its insights", async () => {
    const wrapper = await mountView()

    const shown = shownInsights(wrapper)
    expect(shown[insights.distanceInRange]).toBe('60.0 km')
    expect(shown[insights.avgTripLength]).toBe('20.0 km')
    expect(shown[insights.climateUsage]).toBe('25%')
    expect(shown[insights.parkingLocations]).toBe('2')
    expect(shown[insights.batteryVoltageTrend]).toBe('12.6 V')
  })

  it('leaves out the electric share for a car without a plug', async () => {
    const wrapper = await mountView()

    expect(shownInsights(wrapper)).not.toHaveProperty(insights.electricShare)
  })

  it("shows a plug-in hybrid's electric share of today's driving", async () => {
    vi.mocked(vehicleApi.list).mockResolvedValue([car('phev')])
    vi.mocked(vehicleApi.status).mockResolvedValue({
      ...parked,
      mileageOfTheDay: 40,
      mileageSinceLastCharge: 30,
    } as TelemetrySnapshot)

    const wrapper = await mountView()

    expect(shownInsights(wrapper)[insights.electricShare]).toBe('75%')
  })

  it('shows where the car parked once the insight is opened', async () => {
    const wrapper = await mountView()

    await wrapper.find('.status-card--clickable').trigger('click')

    // The modal is teleported to the page's body.
    expect(document.body.querySelectorAll('.marker')).toHaveLength(2)
  })

  it('loads the new days when the period changes, with a month table for a long one', async () => {
    const wrapper = await mountView()
    expect(wrapper.find('.month-table').exists()).toBe(false)
    vi.mocked(vehicleApi.dailyStats).mockClear()

    useUiSettingsStore(pinia).statsPeriod = { preset: 'last90', from: null, to: null }
    await flushPromises()

    const [, from, to] = vi.mocked(vehicleApi.dailyStats).mock.calls[0]!
    expect(from).toBe('2026-07-21')
    expect(to).toBe('2026-10-19')
    expect(wrapper.find('.month-table').exists()).toBe(true)
  })

  it('loads again once a trip ends', async () => {
    await mountView()
    vi.mocked(vehicleApi.tripSummaries).mockClear()

    useVehicleStore(pinia).notifyTripCompleted()
    await flushPromises()

    expect(vehicleApi.tripSummaries).toHaveBeenCalledOnce()
  })

  it('says there is no data yet for a car that has reported nothing', async () => {
    vi.mocked(vehicleApi.status).mockResolvedValue(noStatus)
    vi.mocked(vehicleApi.tripSummaries).mockResolvedValue([])

    const wrapper = await mountView()

    expect(wrapper.find('.empty-state').text()).toBe(en.dashboard.noData)
    expect(wrapper.find('.status-card').exists()).toBe(false)
  })

  it('says something went wrong when the days cannot be loaded', async () => {
    vi.mocked(vehicleApi.status).mockResolvedValue(noStatus)
    vi.mocked(vehicleApi.tripSummaries).mockResolvedValue([])
    vi.mocked(vehicleApi.dailyStats).mockRejectedValue(new ApiError(500, '/daily-stats'))

    const wrapper = await mountView()

    expect(wrapper.find('.empty-state[role="alert"]').exists()).toBe(true)
  })
})
