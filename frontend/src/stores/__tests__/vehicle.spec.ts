import { describe, it, expect, beforeEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useVehicleStore } from '@/stores/vehicle'

vi.mock('@/services/api', () => ({
  vehicleApi: {
    list: vi.fn().mockResolvedValue([]),
    status: vi.fn().mockResolvedValue(null),
    config: vi.fn().mockResolvedValue({}),
    history: vi.fn().mockResolvedValue([]),
    trips: vi.fn().mockResolvedValue([]),
    sendCommand: vi.fn().mockResolvedValue(undefined),
  },
}))

describe('useVehicleStore - detectedVehicleType', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('returns unknown when hw_version is absent', () => {
    const store = useVehicleStore()
    expect(store.detectedVehicleType).toBe('unknown')
  })

  it('detects phev from hw_version containing PHEV', () => {
    const store = useVehicleStore()
    store.vehicleConfig['hw_version'] = 'MG_PHEV_1.2'
    expect(store.detectedVehicleType).toBe('phev')
  })

  it('detects hev from hw_version containing HEV (not PHEV)', () => {
    const store = useVehicleStore()
    store.vehicleConfig['hw_version'] = 'MG_HEV_1.0'
    expect(store.detectedVehicleType).toBe('hev')
  })

  it('detects bev from hw_version containing BEV', () => {
    const store = useVehicleStore()
    store.vehicleConfig['hw_version'] = 'MG_BEV_1.0'
    expect(store.detectedVehicleType).toBe('bev')
  })

  it('detects bev from hw_version containing EV', () => {
    const store = useVehicleStore()
    store.vehicleConfig['hw_version'] = 'ZS_EV_SERIES_2'
    expect(store.detectedVehicleType).toBe('bev')
  })

  it('returns unknown for an unrecognised hw_version string', () => {
    const store = useVehicleStore()
    store.vehicleConfig['hw_version'] = 'SOME_UNKNOWN_MODEL'
    expect(store.detectedVehicleType).toBe('unknown')
  })

  it('is case-insensitive (lowercased input)', () => {
    const store = useVehicleStore()
    store.vehicleConfig['hw_version'] = 'mg_phev_1.2'
    expect(store.detectedVehicleType).toBe('phev')
  })

  it('prefers phev over hev when hw_version contains PHEV', () => {
    const store = useVehicleStore()
    store.vehicleConfig['hw_version'] = 'PHEV_HEV_COMBO'
    expect(store.detectedVehicleType).toBe('phev')
  })
})

describe('useVehicleStore - fetchVehicles', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('populates vehicles on success', async () => {
    const { vehicleApi } = await import('@/services/api')
    vi.mocked(vehicleApi.list).mockResolvedValue([
      { id: 1, vin: 'ABC123', model: 'MG ZS EV', series: null, saicUser: null, configJson: null, createdAt: '' },
    ])
    const store = useVehicleStore()
    await store.fetchVehicles()
    expect(store.vehicles).toHaveLength(1)
    expect(store.vehicles[0].vin).toBe('ABC123')
  })

  it('sets error on failure', async () => {
    const { vehicleApi } = await import('@/services/api')
    vi.mocked(vehicleApi.list).mockRejectedValue(new Error('Network error'))
    const store = useVehicleStore()
    await store.fetchVehicles()
    expect(store.error).toContain('Network error')
    expect(store.vehicles).toHaveLength(0)
  })

  it('clears loading flag after fetch completes', async () => {
    const store = useVehicleStore()
    await store.fetchVehicles()
    expect(store.loading).toBe(false)
  })
})
