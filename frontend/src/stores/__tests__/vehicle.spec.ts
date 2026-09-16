import { describe, it, expect, beforeEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useVehicleStore } from '@/stores/vehicle'
import { useUiSettingsStore } from '@/stores/settingsUi'
import type { Vehicle, TelemetrySnapshot, TelemetryHistoryPoint, Trip } from '@/services/vehicleApi'

vi.mock('@/services/vehicleApi', () => ({
  vehicleApi: {
    list: vi.fn<() => Promise<Vehicle[]>>().mockResolvedValue([]),
    status: vi.fn<() => Promise<TelemetrySnapshot | null>>().mockResolvedValue(null),
    config: vi.fn<() => Promise<Record<string, string>>>().mockResolvedValue({}),
    history: vi.fn<() => Promise<TelemetryHistoryPoint[]>>().mockResolvedValue([]),
    trips: vi.fn<() => Promise<Trip[]>>().mockResolvedValue([]),
    sendCommand: vi.fn<() => Promise<void>>().mockResolvedValue(undefined),
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
})

describe('useVehicleStore - fetchVehicles', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('populates vehicles on success', async () => {
    const { vehicleApi } = await import('@/services/vehicleApi')
    vi.mocked(vehicleApi.list).mockResolvedValue([
      { id: 1, vin: 'ABC123', model: 'MG ZS EV', series: null, createdAt: '', vehicleType: 'bev' },
    ])
    const store = useVehicleStore()
    await store.fetchVehicles()
    expect(store.vehicles).toHaveLength(1)
    const firstVehicle = store.vehicles[0]
    expect(firstVehicle).toBeDefined()
    if (!firstVehicle) throw new Error('Expected first vehicle to exist')
    expect(firstVehicle.vin).toBe('ABC123')
  })

  it('sets error on failure', async () => {
    const { vehicleApi } = await import('@/services/vehicleApi')
    vi.mocked(vehicleApi.list).mockRejectedValue(new Error('Network error'))
    const store = useVehicleStore()
    await store.fetchVehicles()
    expect(store.vehiclesError).toContain('Network error')
    expect(store.vehicles).toHaveLength(0)
  })

  it('clears loading flag after fetch completes', async () => {
    const store = useVehicleStore()
    await store.fetchVehicles()
    expect(store.loading).toBe(false)
  })
})
