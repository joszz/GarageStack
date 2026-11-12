import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { enableAutoUnmount, flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia, type Pinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import TripLogView from '../TripLogView.vue'
import { tripLogApi, type TripLogEntry } from '@/services/tripLogApi'
import { commutePlacesApi } from '@/services/commutePlacesApi'
import { vehicleApi, type Vehicle } from '@/services/vehicleApi'
import { downloadCsv } from '@/utils/download'
import { useUiSettingsStore } from '@/stores/settingsUi'
import { logEntry } from '@/services/__tests__/tripLogFixtures'
import { WIDE_MEDIA_QUERY } from '@/utils/viewport'

vi.mock('@/services/tripLogApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/services/tripLogApi')>()
  return {
    ...actual,
    tripLogApi: {
      log: vi.fn<typeof actual.tripLogApi.log>(),
      update: vi.fn<typeof actual.tripLogApi.update>(),
      setPurpose: vi.fn<typeof actual.tripLogApi.setPurpose>(),
      setHidden: vi.fn<typeof actual.tripLogApi.setHidden>(),
      mergeWithNext: vi.fn<typeof actual.tripLogApi.mergeWithNext>(),
      resolvePlaces: vi.fn<typeof actual.tripLogApi.resolvePlaces>(),
      editor: vi.fn<typeof actual.tripLogApi.editor>(),
    },
  }
})

vi.mock('@/services/commutePlacesApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/services/commutePlacesApi')>()
  return {
    ...actual,
    commutePlacesApi: {
      ...actual.commutePlacesApi,
      list: vi.fn<typeof actual.commutePlacesApi.list>(),
    },
  }
})

vi.mock('@/services/vehicleApi', () => ({
  vehicleApi: { list: vi.fn<() => Promise<Vehicle[]>>() },
}))

vi.mock('@/utils/download', () => ({ downloadCsv: vi.fn<typeof downloadCsv>() }))

// The page looks up addresses in rounds with pauses between them, and stops when it closes; left
// open, a test's page would carry on asking in the tests after it.
enableAutoUnmount(afterEach)

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })
const car = { id: 1, vin: 'FAKEVN00000000001', vehicleType: 'hev' } as Vehicle

// Oldest first, as the API answers; the page lists them newest first.
const trips: TripLogEntry[] = [
  logEntry({ id: 1, startedAt: '2026-10-01T07:00:00Z', purpose: 'business' }),
  logEntry({ id: 2, startedAt: '2026-10-02T07:00:00Z', isHidden: true, notes: 'GPS glitch' }),
  logEntry({ id: 3, startedAt: '2026-10-03T07:00:00Z', purpose: 'private' }),
]

async function mountView() {
  const wrapper = mount(TripLogView, {
    global: {
      plugins: [i18n, pinia],
      stubs: {
        FontAwesomeIcon: true,
        CommutePlacesModal: true,
        TripRouteMap: true,
        StatsChartCard: true,
        RouterLink: true,
        teleport: true,
      },
    },
  })
  await flushPromises()
  return wrapper
}

function listedIds(wrapper: Awaited<ReturnType<typeof mountView>>) {
  return wrapper.findAll('.trip-log-item').map((item) => item.find('.trip-log-item__date').text())
}

function button(wrapper: Awaited<ReturnType<typeof mountView>>, text: string) {
  return wrapper.findAll('button').filter((b) => b.text() === text)
}

// Picks the trip at this place in the list, which opens its detail.
async function pickTrip(wrapper: Awaited<ReturnType<typeof mountView>>, index: number) {
  await wrapper.findAll('.trip-log-item__select')[index]!.trigger('click')
  await flushPromises()
}

// A window wide enough for the detail beside the list.
function wideWindow() {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: query === WIDE_MEDIA_QUERY,
    media: query,
    addEventListener: () => {},
    removeEventListener: () => {},
  }))
}

// Handed to the mount and to every store the test reads, rather than left to the active pinia,
// which a busy test run has been seen to switch between a mount and its assertions.
let pinia: Pinia

describe('TripLogView', () => {
  beforeEach(() => {
    pinia = createPinia()
    setActivePinia(pinia)
    vi.clearAllMocks()
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(new Date('2026-10-19T12:00:00Z'))
    vi.mocked(vehicleApi.list).mockResolvedValue([car])
    vi.mocked(commutePlacesApi.list).mockResolvedValue([])
    vi.mocked(tripLogApi.log).mockResolvedValue(trips)
    vi.mocked(tripLogApi.editor).mockResolvedValue({
      id: 1,
      points: [],
      originalPoints: null,
      editedAt: null,
      previousStop: null,
      nextStop: null,
      suggestion: false,
      notBefore: null,
      notAfter: null,
      maxPoints: 2000,
    })
    vi.mocked(tripLogApi.resolvePlaces).mockResolvedValue({
      trips: [],
      available: true,
      hasMore: false,
    })
  })

  afterEach(() => {
    vi.useRealTimers()
    vi.unstubAllGlobals()
  })

  it("loads this month's trips for the car, newest first, without the hidden one", async () => {
    const wrapper = await mountView()

    const [vin, from, to] = vi.mocked(tripLogApi.log).mock.calls[0]!
    expect(vin).toBe(car.vin)
    expect(new Date(from).getMonth()).toBe(9)
    expect(new Date(to).getTime()).toBeGreaterThan(new Date(from).getTime())
    expect(listedIds(wrapper)).toEqual(['Sat, 3 Oct', 'Thu, 1 Oct'])
  })

  it('folds the purposes and driving figures behind one button, which says what it does', async () => {
    const wrapper = await mountView()
    const toggle = wrapper.get('.trip-log-figures__toggle')

    expect(toggle.attributes('aria-expanded')).toBe('false')
    expect(toggle.text()).toBe(en.tripLog.moreFigures)
    expect(wrapper.find('.trip-log-figures--open').exists()).toBe(false)

    await toggle.trigger('click')

    expect(toggle.attributes('aria-expanded')).toBe('true')
    expect(toggle.text()).toBe(en.tripLog.fewerFigures)
    expect(wrapper.find('.trip-log-figures--open').exists()).toBe(true)
    // Both folded sections are named as what the button controls.
    for (const id of toggle.attributes('aria-controls')!.split(' ')) {
      expect(wrapper.find(`[id="${id}"]`).exists()).toBe(true)
    }
  })

  it('lists the hidden trips once asked to, and counts them on the switch', async () => {
    const wrapper = await mountView()
    const toggle = wrapper.find('.trip-log-hidden-toggle')

    expect(toggle.text()).toBe('Show hidden trip')
    await toggle.find('input').setValue(true)

    expect(listedIds(wrapper)).toEqual(['Sat, 3 Oct', 'Fri, 2 Oct', 'Thu, 1 Oct'])
    expect(wrapper.find('.trip-log-item--hidden').exists()).toBe(true)
  })

  it('leaves the hidden trip out of the totals and the export', async () => {
    const wrapper = await mountView()

    await button(wrapper, 'Export CSV')[0]!.trigger('click')

    const csv = vi.mocked(downloadCsv).mock.calls[0]![1]
    expect(csv).not.toContain('GPS glitch')
    expect(csv.trim().split('\n')).toHaveLength(3)
    expect(wrapper.find('.trip-log-totals').text()).toContain('Unclassified (0)')
  })

  it('joins a trip with the next one once confirmed, and offers none for the newest', async () => {
    vi.mocked(tripLogApi.mergeWithNext).mockResolvedValue(logEntry({ id: 1, distanceKm: 80 }))
    const wrapper = await mountView()

    await pickTrip(wrapper, 0)
    expect(button(wrapper, 'Join with next trip')).toHaveLength(0)
    await pickTrip(wrapper, 1)
    await button(wrapper, 'Join with next trip')[0]!.trigger('click')
    expect(tripLogApi.mergeWithNext).not.toHaveBeenCalled()

    await button(wrapper, 'Join trips')[0]!.trigger('click')
    await flushPromises()

    expect(tripLogApi.mergeWithNext).toHaveBeenCalledWith(car.vin, 1)
    expect(button(wrapper, 'Join trips')).toHaveLength(0)
  })

  it('leaves the trips as they are when the join is cancelled', async () => {
    const wrapper = await mountView()

    await pickTrip(wrapper, 1)
    await button(wrapper, 'Join with next trip')[0]!.trigger('click')
    await button(wrapper, 'Cancel')[0]!.trigger('click')

    expect(tripLogApi.mergeWithNext).not.toHaveBeenCalled()
    expect(button(wrapper, 'Join trips')).toHaveLength(0)
  })

  it('hides a trip, which then leaves the list', async () => {
    vi.mocked(tripLogApi.setHidden).mockResolvedValue({ ...trips[2]!, isHidden: true })
    const wrapper = await mountView()

    await pickTrip(wrapper, 0)
    await button(wrapper, 'Hide')[0]!.trigger('click')
    await flushPromises()

    expect(tripLogApi.setHidden).toHaveBeenCalledWith(car.vin, 3, true)
    expect(listedIds(wrapper)).toEqual(['Thu, 1 Oct'])
    expect(wrapper.find('.trip-log-hidden-toggle').text()).toBe('Show 2 hidden trips')
  })

  it('loads the whole year when the month is cleared', async () => {
    const wrapper = await mountView()

    // The first option, "Whole year", has no month.
    const month = wrapper.findAll('select')[1]!
    ;(month.element as HTMLSelectElement).selectedIndex = 0
    await month.trigger('change')
    await flushPromises()

    const [, from, to] = vi.mocked(tripLogApi.log).mock.lastCall!
    expect(new Date(from).getMonth()).toBe(0)
    expect(new Date(to).getFullYear()).toBe(2027)
  })

  it('asks for no addresses while place names are switched off', async () => {
    pinia = createPinia()
    setActivePinia(pinia)
    useUiSettingsStore(pinia).placeNamesEnabled = false

    const wrapper = await mountView()

    expect(tripLogApi.resolvePlaces).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain(en.tripLog.placesOff)
  })

  it('totals all trips and each purpose with its share of the distance', async () => {
    const wrapper = await mountView()
    const totals = wrapper.find('.trip-log-totals').text()

    expect(totals).toContain('All trips (2)')
    expect(totals).toContain('85.0km')
    expect(totals).toContain('Business (1)')
    expect(totals).toContain('50% of the distance')
    expect(wrapper.findAll('.trip-log-totals progress')).toHaveLength(4)
  })

  it('sums up the driving: time, average and longest trip, average speed', async () => {
    vi.mocked(tripLogApi.log).mockResolvedValue([
      logEntry({ id: 1, startedAt: '2026-10-01T07:00:00Z', endedAt: '2026-10-01T07:34:00Z' }),
      logEntry({ id: 3, startedAt: '2026-10-03T07:00:00Z', endedAt: '2026-10-03T07:34:00Z' }),
    ])
    const wrapper = await mountView()
    const summary = wrapper.find('.trip-log-summary').text()

    // Two trips of 34 minutes and 42.5 km each.
    expect(summary).toContain('1h 8m')
    expect(summary).toContain('42.5km')
    expect(summary).toContain('75km/h')
  })

  it('says how far the unclassified trips went', async () => {
    vi.mocked(tripLogApi.log).mockResolvedValue([logEntry({ id: 1 }), logEntry({ id: 2 })])
    const wrapper = await mountView()

    expect(wrapper.find('.trip-log-bulk').text()).toContain(
      '2 trips (85.0 km) have no purpose yet. Mark them all as:',
    )
  })

  it('shows no detail on a narrow screen until a trip is picked, then in a dialog', async () => {
    const wrapper = await mountView()

    expect(wrapper.find('.trip-log-detail').exists()).toBe(false)
    await pickTrip(wrapper, 1)

    expect(wrapper.find('.detail-modal .trip-log-detail').exists()).toBe(true)
    expect(tripLogApi.editor).toHaveBeenCalledWith(car.vin, 1)
    expect(wrapper.findAll('.trip-log-item')[1]!.classes()).toContain('trip-log-item--selected')
  })

  it('shows the newest trip beside the list on a wide screen, until another is picked', async () => {
    wideWindow()
    const wrapper = await mountView()
    const detail = () => wrapper.find('.trip-log-layout__detail .trip-log-detail')

    expect(detail().exists()).toBe(true)
    expect(wrapper.find('.detail-modal .trip-log-detail').exists()).toBe(false)
    expect(tripLogApi.editor).toHaveBeenLastCalledWith(car.vin, 3)

    await pickTrip(wrapper, 1)

    expect(tripLogApi.editor).toHaveBeenLastCalledWith(car.vin, 1)
    expect(wrapper.findAll('.trip-log-item')[1]!.classes()).toContain('trip-log-item--selected')
  })
})
