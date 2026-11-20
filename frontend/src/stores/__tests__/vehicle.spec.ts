import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { nextTick } from 'vue'
import { setActivePinia, createPinia } from 'pinia'
import { flushPromises } from '@vue/test-utils'
import { useVehicleStore } from '@/stores/vehicle'
import { useUiSettingsStore } from '@/stores/settingsUi'
import { vehicleApi } from '@/services/vehicleApi'
import { readLastVin } from '@/utils/lastVin'
import { readLastKnownState, saveLastKnownState } from '@/utils/lastKnownState'
import type {
  Vehicle,
  TelemetrySnapshot,
  DailyStat,
  Trip,
  TripSummary,
  VehicleSettings,
  VehicleSettingsValues,
} from '@/services/vehicleApi'

const { noSettings } = vi.hoisted(() => ({
  noSettings: {
    hvBatteryCapacityKwh: null,
    tyreLowBar: null,
    tyreGoodBar: null,
    tyreHighBar: null,
    tyreDropBar: null,
    auxBatteryLowVolt: null,
    lowFuelPercent: null,
    lowEvPercent: null,
    vehicleType: null,
  },
}))

vi.mock('@/services/vehicleApi', () => ({
  vehicleApi: {
    list: vi.fn<() => Promise<Vehicle[]>>().mockResolvedValue([]),
    status: vi.fn<() => Promise<TelemetrySnapshot | null>>().mockResolvedValue(null),
    config: vi.fn<() => Promise<Record<string, string>>>().mockResolvedValue({}),
    dailyStats: vi.fn<() => Promise<DailyStat[]>>().mockResolvedValue([]),
    trips: vi.fn<() => Promise<Trip[]>>().mockResolvedValue([]),
    tripSummaries: vi.fn<() => Promise<TripSummary[]>>().mockResolvedValue([]),
    latestTrip: vi.fn<() => Promise<Trip | undefined>>().mockResolvedValue(undefined),
    sendCommand: vi.fn<() => Promise<void>>().mockResolvedValue(undefined),
    settings: vi.fn<(vin: string) => Promise<VehicleSettings>>(),
    updateSettings: vi
      .fn<(vin: string, changes: Partial<VehicleSettingsValues>) => Promise<VehicleSettings>>()
      .mockResolvedValue({ values: noSettings, defaults: noSettings }),
    setPowerTailgate: vi
      .fn<(vin: string, enabled: boolean) => Promise<{ enabled: boolean }>>()
      .mockImplementation((_vin, enabled) => Promise.resolve({ enabled })),
  },
}))

function vehicle(vehicleType: Vehicle['vehicleType']): Vehicle {
  return {
    id: 1,
    vin: 'FAKEVN00000000001',
    model: 'MG',
    series: null,
    createdAt: '2026-01-01T00:00:00Z',
    vehicleType,
    vehicleTypeOverride: null,
    hvBatteryCapacityKwh: null,
    supportsTargetSoc: false,
    hasPowerTailgate: false,
  }
}

describe('useVehicleStore - detectedVehicleType', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('returns unknown before any vehicle has been fetched', () => {
    const store = useVehicleStore()
    expect(store.detectedVehicleType).toBe('unknown')
  })

  it('reports the type the API detected for the active vehicle', () => {
    const store = useVehicleStore()
    store.vehicles = [vehicle('phev')]
    expect(store.detectedVehicleType).toBe('phev')
  })

  it('returns unknown when the API could not detect a type', () => {
    const store = useVehicleStore()
    store.vehicles = [vehicle('unknown')]
    expect(store.detectedVehicleType).toBe('unknown')
  })
})

describe('useVehicleStore - setPowerTailgate', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('saves it and updates the vehicle in the list without fetching it again', async () => {
    const store = useVehicleStore()
    store.vehicles = [vehicle('bev')]

    await store.setPowerTailgate('FAKEVN00000000001', true)

    expect(vehicleApi.setPowerTailgate).toHaveBeenCalledWith('FAKEVN00000000001', true)
    expect(store.activeVehicle?.hasPowerTailgate).toBe(true)
    expect(vehicleApi.list).not.toHaveBeenCalled()
  })
})

describe('useVehicleStore - activeVehicle', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('is null while the vehicle list is empty', () => {
    const store = useVehicleStore()
    expect(store.activeVehicle).toBeNull()
    expect(store.activeVin).toBeNull()
  })

  it('is the first vehicle in the list', () => {
    const store = useVehicleStore()
    store.vehicles = [vehicle('bev'), { ...vehicle('hev'), id: 2, vin: 'FAKEVN00000000002' }]
    expect(store.activeVehicle?.id).toBe(1)
    expect(store.activeVin).toBe('FAKEVN00000000001')
  })
})

describe('useVehicleStore - effectiveVehicleType', () => {
  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
  })

  it('follows the detected type while the override is auto', () => {
    const store = useVehicleStore()
    store.vehicles = [vehicle('phev')]
    expect(store.effectiveVehicleType).toBe('phev')
  })

  it('uses the manual override when one is set', () => {
    const store = useVehicleStore()
    const ui = useUiSettingsStore()
    store.vehicles = [vehicle('phev')]
    ui.vehicleTypeOverride = 'hev'
    expect(store.effectiveVehicleType).toBe('hev')
  })

  it("takes the type set in the car's settings over the browser's older one", () => {
    const store = useVehicleStore()
    useUiSettingsStore().vehicleTypeOverride = 'hev'
    store.vehicles = [{ ...vehicle('bev'), vehicleTypeOverride: 'phev' }]
    expect(store.effectiveVehicleType).toBe('phev')
  })

  // The browser's older choice belongs to no car in particular, so it means nothing with two.
  it("ignores the browser's older type once there is more than one car", () => {
    const store = useVehicleStore()
    useUiSettingsStore().vehicleTypeOverride = 'hev'
    store.vehicles = [vehicle('bev'), { ...vehicle('phev'), id: 2, vin: 'FAKEVN00000000002' }]
    expect(store.effectiveVehicleType).toBe('bev')
  })

  it('saves settings with the car and fetches the list again for the values in effect', async () => {
    const store = useVehicleStore()
    store.vehicles = [vehicle('bev')]
    vi.mocked(vehicleApi.list).mockResolvedValueOnce([
      { ...vehicle('bev'), vehicleTypeOverride: 'phev', hvBatteryCapacityKwh: 21 },
    ])

    await store.saveSettings('FAKEVN00000000001', { vehicleType: 'phev' })

    expect(vehicleApi.updateSettings).toHaveBeenCalledWith('FAKEVN00000000001', {
      vehicleType: 'phev',
    })
    expect(store.effectiveVehicleType).toBe('phev')
    expect(store.hvBatteryCapacityKwh).toBe(21)
  })
})

// A type chosen in the browser before the type moved to the car, saved to the car once.
describe('useVehicleStore - moving the browser type to the car', () => {
  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
    vi.mocked(vehicleApi.updateSettings).mockClear()
  })

  afterEach(() => {
    vi.mocked(vehicleApi.list).mockResolvedValue([])
  })

  async function listed(...cars: Vehicle[]) {
    vi.mocked(vehicleApi.list).mockResolvedValue(cars)
    const store = useVehicleStore()
    await store.fetchVehicles(true)
    await flushPromises()
    return store
  }

  it("saves a one-car install's browser type to the car and clears the browser one", async () => {
    useUiSettingsStore().vehicleTypeOverride = 'phev'

    await listed(vehicle('hev'))

    expect(vehicleApi.updateSettings).toHaveBeenCalledExactlyOnceWith('FAKEVN00000000001', {
      vehicleType: 'phev',
    })
    expect(useUiSettingsStore().vehicleTypeOverride).toBe('auto')
  })

  it('clears the battery size along with it when the type is a plain hybrid', async () => {
    useUiSettingsStore().vehicleTypeOverride = 'hev'

    await listed(vehicle('phev'))

    expect(vehicleApi.updateSettings).toHaveBeenCalledExactlyOnceWith('FAKEVN00000000001', {
      vehicleType: 'hev',
      hvBatteryCapacityKwh: null,
    })
  })

  it('leaves it alone with auto, two cars, or a type the car already has', async () => {
    await listed(vehicle('hev'))
    useUiSettingsStore().vehicleTypeOverride = 'phev'
    await listed(vehicle('hev'), { ...vehicle('bev'), id: 2, vin: 'FAKEVN00000000002' })
    await listed({ ...vehicle('hev'), vehicleTypeOverride: 'bev' })

    expect(vehicleApi.updateSettings).not.toHaveBeenCalled()
    expect(useUiSettingsStore().vehicleTypeOverride).toBe('phev')
  })

  it('keeps the browser type when the save fails, to try again later', async () => {
    useUiSettingsStore().vehicleTypeOverride = 'phev'
    vi.mocked(vehicleApi.updateSettings).mockRejectedValueOnce(new TypeError('offline'))

    await listed(vehicle('hev'))

    expect(useUiSettingsStore().vehicleTypeOverride).toBe('phev')
  })
})

describe('useVehicleStore - hvBatteryCapacityKwh', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('is null before a vehicle is fetched and when the deployment configured none', () => {
    const store = useVehicleStore()
    expect(store.hvBatteryCapacityKwh).toBeNull()

    store.vehicles = [vehicle('hev')]
    expect(store.hvBatteryCapacityKwh).toBeNull()
  })

  it('reports the configured capacity for the active vehicle', () => {
    const store = useVehicleStore()
    store.vehicles = [{ ...vehicle('hev'), hvBatteryCapacityKwh: 1.83 }]
    expect(store.hvBatteryCapacityKwh).toBe(1.83)
  })
})

describe('useVehicleStore - energyCounterScale', () => {
  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
  })

  // Issue #570: the counters come on the pack the car reports, MG's nominal 72.5 kWh on a PHEV.
  it('rescales from the reported pack to the configured one', () => {
    const store = useVehicleStore()
    store.vehicles = [{ ...vehicle('phev'), hvBatteryCapacityKwh: 29 }]
    store.applyLiveStatus({
      recordedAt: '2026-11-18T12:00:00Z',
      hvTotalCapacityKwh: 72.5,
    } as TelemetrySnapshot)
    expect(store.energyCounterScale).toEqual({ unit: 'kwh', factor: 0.4 })
  })

  it('has no kWh for a plug-in hybrid before the reported pack is known', () => {
    const store = useVehicleStore()
    store.vehicles = [{ ...vehicle('phev'), hvBatteryCapacityKwh: 29 }]
    expect(store.energyCounterScale.factor).toBeNull()
  })
})

describe('useVehicleStore - fetchVehicles', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('populates vehicles on success', async () => {
    const { vehicleApi } = await import('@/services/vehicleApi')
    vi.mocked(vehicleApi.list).mockResolvedValue([
      {
        id: 1,
        vin: 'FAKEVN00000000002',
        model: 'MG ZS EV',
        series: null,
        createdAt: '',
        vehicleType: 'bev',
        vehicleTypeOverride: null,
        hvBatteryCapacityKwh: null,
        supportsTargetSoc: false,
        hasPowerTailgate: false,
      },
    ])
    const store = useVehicleStore()
    await store.fetchVehicles()
    expect(store.vehicles).toHaveLength(1)
    const firstVehicle = store.vehicles[0]
    expect(firstVehicle).toBeDefined()
    if (!firstVehicle) throw new Error('Expected first vehicle to exist')
    expect(firstVehicle.vin).toBe('FAKEVN00000000002')
  })

  it('sets error on failure', async () => {
    const { vehicleApi } = await import('@/services/vehicleApi')
    vi.mocked(vehicleApi.list).mockRejectedValue(new Error('Network error'))
    const store = useVehicleStore()
    await store.fetchVehicles()
    expect(store.vehiclesError?.message).toContain('Network error')
    expect(store.vehicles).toHaveLength(0)
  })

  it('clears loading flag after fetch completes', async () => {
    const store = useVehicleStore()
    await store.fetchVehicles()
    expect(store.loading).toBe(false)
  })

  it("remembers the car's VIN, for the next page load to ask for its status sooner", async () => {
    localStorage.clear()
    vi.mocked(vehicleApi.list).mockResolvedValue([vehicle('bev')])

    await useVehicleStore().fetchVehicles()

    expect(readLastVin()).toBe('FAKEVN00000000001')
  })
})

describe('useVehicleStore - answers that bring nothing new', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  const reading = { recordedAt: '2026-11-18T12:00:00Z', evSocPercent: 71 } as TelemetrySnapshot

  it('keeps the status it has when the same reading comes again, so nothing renders again', async () => {
    const store = useVehicleStore()
    vi.mocked(vehicleApi.status).mockResolvedValueOnce(reading)
    await store.fetchStatus('FAKEVN00000000001')
    const held = store.currentStatus

    vi.mocked(vehicleApi.status).mockResolvedValueOnce({ ...reading })
    await store.fetchStatus('FAKEVN00000000001')
    expect(store.currentStatus).toBe(held)

    store.applyLiveStatus({ ...reading })
    expect(store.currentStatus).toBe(held)
  })

  it('takes a reading that differs', async () => {
    const store = useVehicleStore()
    vi.mocked(vehicleApi.status).mockResolvedValueOnce(reading)
    await store.fetchStatus('FAKEVN00000000001')

    store.applyLiveStatus({ ...reading, evSocPercent: 70 })

    expect(store.currentStatus?.evSocPercent).toBe(70)
  })

  it('keeps the latest trip it has when the same trip comes again', async () => {
    const store = useVehicleStore()
    const trip = { index: 0, id: 7, points: [] } as unknown as Trip
    vi.mocked(vehicleApi.latestTrip).mockResolvedValueOnce(trip)
    await store.fetchLatestTrip('FAKEVN00000000001')
    const held = store.latestTrip

    vi.mocked(vehicleApi.latestTrip).mockResolvedValueOnce({ ...trip })
    await store.fetchLatestTrip('FAKEVN00000000001')

    expect(store.latestTrip).toBe(held)
  })
})

describe('useVehicleStore - applyCommandResult', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('is null before the gateway has answered anything', () => {
    expect(useVehicleStore().lastCommandResult).toBeNull()
  })

  it('holds the latest answer for the command composables to pick up', () => {
    const store = useVehicleStore()
    const result = { command: 'lock', success: false, detail: 'vehicle is not online' }

    store.applyCommandResult(result)

    expect(store.lastCommandResult).toEqual(result)
  })
})

describe('useVehicleStore - trips for the dashboard and statistics', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('keeps the latest trip, and none when the vehicle has none', async () => {
    const store = useVehicleStore()
    const latest = { index: 0, id: null, points: [] } as unknown as Trip
    vi.mocked(vehicleApi.latestTrip).mockResolvedValueOnce(latest)

    await store.fetchLatestTrip('FAKEVN00000000001')
    expect(store.latestTrip).toEqual(latest)

    vi.mocked(vehicleApi.latestTrip).mockResolvedValueOnce(undefined)
    await store.fetchLatestTrip('FAKEVN00000000001')
    expect(store.latestTrip).toBeNull()
  })

  it('keeps trip summaries apart from the map trips', async () => {
    const store = useVehicleStore()
    const summaries = [{ index: 0, id: 1, distanceKm: 12 }] as unknown as TripSummary[]
    vi.mocked(vehicleApi.tripSummaries).mockResolvedValueOnce(summaries)

    await store.fetchTripSummaries('FAKEVN00000000001', '2026-01-01T00:00:00Z')

    expect(store.tripSummaries).toEqual(summaries)
    expect(store.trips).toEqual([])
  })
})

describe('useVehicleStore - fetchTodayStat', () => {
  const VIN = 'FAKEVN00000000001'
  const day = (sinceChargeTotal: number) =>
    ({ date: '2026-11-08', sinceChargeTotal, counterUnbacked: true }) as DailyStat

  beforeEach(() => {
    setActivePinia(createPinia())
    vi.mocked(vehicleApi.dailyStats).mockReset()
  })

  it("asks for today only and keeps today's row", async () => {
    const store = useVehicleStore()
    vi.mocked(vehicleApi.dailyStats).mockResolvedValueOnce([day(17.9)])

    await store.fetchTodayStat(VIN)

    const [, from, to] = vi.mocked(vehicleApi.dailyStats).mock.calls[0]!
    expect(from).toBe(to)
    expect(store.todayStat?.sinceChargeTotal).toBe(17.9)
  })

  it('keeps the answer to the latest request when answers overlap', async () => {
    const store = useVehicleStore()
    let answerFirst: (days: DailyStat[]) => void = () => {}
    vi.mocked(vehicleApi.dailyStats)
      .mockReturnValueOnce(new Promise((resolve) => (answerFirst = resolve)))
      .mockResolvedValueOnce([day(17.9)])

    const first = store.fetchTodayStat(VIN)
    await store.fetchTodayStat(VIN)
    answerFirst([day(12)])
    await first

    expect(store.todayStat?.sinceChargeTotal).toBe(17.9)
  })

  it("drops the previous car's row and keeps the last one on a failure", async () => {
    const store = useVehicleStore()
    vi.mocked(vehicleApi.dailyStats).mockResolvedValueOnce([day(17.9)])
    await store.fetchTodayStat(VIN)

    vi.mocked(vehicleApi.dailyStats).mockRejectedValueOnce(new Error('offline'))
    await store.fetchTodayStat(VIN)
    expect(store.todayStat?.sinceChargeTotal).toBe(17.9)

    vi.mocked(vehicleApi.dailyStats).mockRejectedValueOnce(new Error('offline'))
    await store.fetchTodayStat('FAKEVN00000000002')
    expect(store.todayStat).toBeNull()
  })
})

describe('useVehicleStore - the car as last loaded, for when the server cannot be reached', () => {
  const saved = {
    vehicles: [vehicle('phev')],
    status: { vehicleId: 1, recordedAt: '2026-11-18T09:00:00Z', latitude: 52 } as TelemetrySnapshot,
    config: { BType: '1' },
    latestTrip: { id: 3, distanceKm: 8, points: [] } as unknown as Trip,
  }

  function signIn() {
    localStorage.setItem('garagestack-auth-username', 'demo')
    localStorage.setItem('garagestack-auth-expires', new Date(Date.now() + 3_600_000).toISOString())
  }

  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('starts from the saved copy, and says so', () => {
    signIn()
    saveLastKnownState(saved)

    const store = useVehicleStore()

    expect(store.activeVin).toBe('FAKEVN00000000001')
    expect(store.currentStatus).toEqual(saved.status)
    expect(store.vehicleConfig).toEqual({ BType: '1' })
    expect(store.latestTrip).toEqual(saved.latestTrip)
    expect(store.showingSavedState).toBe(true)
  })

  it('starts empty without a session, whatever was left in storage', () => {
    saveLastKnownState(saved)

    const store = useVehicleStore()

    expect(store.vehicles).toEqual([])
    expect(store.currentStatus).toBeNull()
    expect(store.showingSavedState).toBe(false)
  })

  it('still asks the server for the vehicle list it started from', async () => {
    signIn()
    saveLastKnownState(saved)
    vi.mocked(vehicleApi.list).mockResolvedValue([{ ...vehicle('phev'), model: 'MG HS' }])
    const store = useVehicleStore()

    await store.fetchVehicles()
    await store.fetchVehicles()

    expect(vehicleApi.list).toHaveBeenCalledOnce()
    expect(store.activeVehicle?.model).toBe('MG HS')
  })

  it('keeps showing the saved car when the server cannot be reached', async () => {
    signIn()
    saveLastKnownState(saved)
    vi.mocked(vehicleApi.list).mockRejectedValue(new TypeError('Failed to fetch'))
    vi.mocked(vehicleApi.status).mockRejectedValue(new TypeError('Failed to fetch'))
    const store = useVehicleStore()

    await store.fetchVehicles()
    await store.fetchStatus('FAKEVN00000000001')

    expect(store.activeVin).toBe('FAKEVN00000000001')
    expect(store.currentStatus).toEqual(saved.status)
    expect(store.statusError).not.toBeNull()
    expect(store.showingSavedState).toBe(true)
  })

  it('stops calling it saved once the server answers, and saves the answer', async () => {
    signIn()
    saveLastKnownState(saved)
    const fresh = { ...saved.status, recordedAt: '2026-11-18T10:00:00Z' }
    vi.mocked(vehicleApi.status).mockResolvedValue(fresh)
    const store = useVehicleStore()

    await store.fetchStatus('FAKEVN00000000001')

    expect(store.showingSavedState).toBe(false)
    expect(readLastKnownState()?.status).toEqual(fresh)
  })

  it('saves a live reading and the newest trip as they come in', async () => {
    vi.mocked(vehicleApi.list).mockResolvedValue([vehicle('bev')])
    vi.mocked(vehicleApi.latestTrip).mockResolvedValue({
      id: 9,
      distanceKm: 4,
      points: [{ recordedAt: '2026-11-18T09:00:00Z', latitude: 52, longitude: 5, speed: 20 }],
    } as unknown as Trip)
    const store = useVehicleStore()
    await store.fetchVehicles()

    store.applyLiveStatus({ ...saved.status, evSocPercent: 64 })
    await store.fetchLatestTrip('FAKEVN00000000001')

    const kept = readLastKnownState()
    expect(kept?.status?.evSocPercent).toBe(64)
    expect(kept?.latestTrip).toMatchObject({ id: 9, points: [] })
  })

  it('saves nothing before the car is known', () => {
    useVehicleStore().applyLiveStatus(saved.status)

    expect(readLastKnownState()).toBeNull()
  })
})

describe('useVehicleStore - choosing a car', () => {
  const first = { ...vehicle('phev'), id: 1, vin: 'FAKEVN00000000001', model: 'MG HS' }
  const second = { ...vehicle('bev'), id: 2, vin: 'FAKEVN00000000002', model: 'MG4' }

  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
  })

  it('follows the first car until another is chosen', () => {
    const store = useVehicleStore()
    store.vehicles = [first, second]
    expect(store.hasMultipleVehicles).toBe(true)
    expect(store.activeVin).toBe(first.vin)

    store.selectVehicle(second.vin)
    expect(store.activeVin).toBe(second.vin)
    expect(store.detectedVehicleType).toBe('bev')
  })

  it('remembers the chosen car on this device', () => {
    useVehicleStore().vehicles = [first, second]
    useVehicleStore().selectVehicle(second.vin)

    setActivePinia(createPinia())
    const reloaded = useVehicleStore()
    reloaded.vehicles = [first, second]
    expect(reloaded.activeVin).toBe(second.vin)
  })

  it('falls back to the first car when the remembered one is gone', () => {
    localStorage.setItem('garagestack-last-vin', 'NOT-ON-THE-ACCOUNT')
    const store = useVehicleStore()
    store.vehicles = [first, second]
    expect(store.activeVin).toBe(first.vin)
  })

  it('ignores a car that is not on the account', () => {
    const store = useVehicleStore()
    store.vehicles = [first, second]
    store.selectVehicle('NOT-ON-THE-ACCOUNT')
    expect(store.activeVin).toBe(first.vin)
  })

  it("drops the previous car's data on a switch", () => {
    const store = useVehicleStore()
    store.vehicles = [first, second]
    store.currentStatus = { isAvailable: true } as TelemetrySnapshot
    store.latestTrip = { id: 7 } as Trip
    store.lastUpdated = new Date()

    store.selectVehicle(second.vin)
    expect(store.currentStatus).toBeNull()
    expect(store.latestTrip).toBeNull()
    expect(store.lastUpdated).toBeNull()
  })

  it('keeps the data when the active car is chosen again', () => {
    const store = useVehicleStore()
    store.vehicles = [first, second]
    const status = { isAvailable: true } as TelemetrySnapshot
    store.currentStatus = status
    store.selectVehicle(first.vin)
    expect(store.currentStatus).toStrictEqual(status)
  })

  it('keeps the colour per car in the browser, and each car its own type', async () => {
    const store = useVehicleStore()
    const ui = useUiSettingsStore()
    store.vehicles = [{ ...first, vehicleTypeOverride: 'phev' }, second]
    await nextTick()

    ui.carColorScheme = 'blue'
    expect(store.effectiveVehicleType).toBe('phev')

    store.selectVehicle(second.vin)
    await nextTick()
    // The second car has no colour of its own yet, and no type set: the detected one stands.
    expect(ui.carColorScheme).toBe('orange')
    expect(store.effectiveVehicleType).toBe('bev')

    ui.carColorScheme = 'red'
    store.selectVehicle(first.vin)
    await nextTick()
    expect(ui.carColorScheme).toBe('blue')
    expect(store.effectiveVehicleType).toBe('phev')
  })

  // A type saved in the browser before it moved to the car belongs to no car in particular.
  it("leaves out the browser's older type once there are two cars", () => {
    const store = useVehicleStore()
    useUiSettingsStore().vehicleTypeOverride = 'hev'
    store.vehicles = [first, second]
    expect(store.effectiveVehicleType).toBe(first.vehicleType)
  })

  it("drops the settings on a switch, and a late answer for the previous car's", async () => {
    const store = useVehicleStore()
    store.vehicles = [first, second]
    const answer: VehicleSettings = { values: noSettings, defaults: noSettings }
    let deliver!: (s: VehicleSettings) => void
    vi.mocked(vehicleApi.settings).mockImplementationOnce(
      () => new Promise<VehicleSettings>((resolve) => (deliver = resolve)),
    )
    store.vehicleSettings = answer

    const pending = store.fetchSettings(first.vin)
    store.selectVehicle(second.vin)
    expect(store.vehicleSettings).toBeNull()

    deliver(answer)
    await pending
    expect(store.vehicleSettings).toBeNull()
  })

  it('drops an answer for the car switched away from while it was on its way', async () => {
    const store = useVehicleStore()
    store.vehicles = [first, second]
    let answer!: (s: TelemetrySnapshot) => void
    vi.mocked(vehicleApi.status).mockImplementationOnce(
      () => new Promise<TelemetrySnapshot>((resolve) => (answer = resolve)),
    )

    const pending = store.fetchStatus(first.vin)
    store.selectVehicle(second.vin)
    answer({ isAvailable: true } as TelemetrySnapshot)
    await pending

    expect(store.currentStatus).toBeNull()
  })
})
