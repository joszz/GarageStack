import { describe, it, expect, beforeEach, vi } from 'vitest'
import { nextTick } from 'vue'
import { createPinia, setActivePinia } from 'pinia'
import { useVehicleStore } from '@/stores/vehicle'
import { useMaintenanceStore } from '@/stores/maintenance'
import { useClimateSchedulesStore } from '@/stores/climateSchedules'
import { useCommutePlacesStore } from '@/stores/commutePlaces'
import { maintenanceApi, type MaintenanceItem } from '@/services/maintenanceApi'
import { climateScheduleApi, type ClimateSchedule } from '@/services/climateScheduleApi'
import { commutePlacesApi, type CommutePlace } from '@/services/commutePlacesApi'
import type { Vehicle } from '@/services/vehicleApi'

vi.mock('@/services/maintenanceApi', () => ({
  maintenanceApi: { list: vi.fn<(vin: string) => Promise<MaintenanceItem[]>>() },
}))
vi.mock('@/services/climateScheduleApi', () => ({
  climateScheduleApi: { list: vi.fn<(vin: string) => Promise<ClimateSchedule[]>>() },
}))
vi.mock('@/services/commutePlacesApi', () => ({
  commutePlacesApi: { list: vi.fn<(vin: string) => Promise<CommutePlace[]>>() },
}))

const A = 'FAKEVN00000000001'
const B = 'FAKEVN00000000002'

function car(id: number, vin: string): Vehicle {
  return {
    id,
    vin,
    model: `Car ${id}`,
    series: null,
    createdAt: '2026-01-01T00:00:00Z',
    vehicleType: 'bev',
    vehicleTypeOverride: null,
    hvBatteryCapacityKwh: null,
    supportsTargetSoc: false,
    hasPowerTailgate: false,
  }
}

/** A list answer that arrives only when the test says so. */
function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((r) => (resolve = r))
  return { promise, resolve }
}

// Each of these stores holds one plain list for the active car.
const cases = [
  {
    name: 'maintenance',
    list: () => vi.mocked(maintenanceApi.list),
    store: () => useMaintenanceStore(),
    fetch: (vin: string) => useMaintenanceStore().fetchItems(vin),
    read: () => useMaintenanceStore().items,
  },
  {
    name: 'climate schedules',
    list: () => vi.mocked(climateScheduleApi.list),
    store: () => useClimateSchedulesStore(),
    fetch: (vin: string) => useClimateSchedulesStore().fetchSchedules(vin),
    read: () => useClimateSchedulesStore().schedules,
  },
  {
    name: 'commute places',
    list: () => vi.mocked(commutePlacesApi.list),
    store: () => useCommutePlacesStore(),
    fetch: (vin: string) => useCommutePlacesStore().fetchPlaces(vin),
    read: () => useCommutePlacesStore().places,
  },
] as const

describe.each(cases)('the $name store with more than one car', (c) => {
  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
    vi.clearAllMocks()
    useVehicleStore().vehicles = [car(1, A), car(2, B)]
    c.store()
  })

  it("drops the previous car's list when another car is picked", async () => {
    c.list().mockResolvedValueOnce([{ id: 1 }] as never)
    await c.fetch(A)
    expect(c.read()).toHaveLength(1)

    useVehicleStore().selectVehicle(B)
    await nextTick()

    expect(c.read()).toEqual([])
  })

  it('ignores an answer for the car switched away from', async () => {
    const slow = deferred<never>()
    c.list().mockReturnValueOnce(slow.promise)
    const pending = c.fetch(A)

    useVehicleStore().selectVehicle(B)
    await nextTick()
    slow.resolve([{ id: 1 }] as never)
    await pending

    expect(c.read()).toEqual([])
  })

  it("keeps the active car's answer", async () => {
    c.list().mockResolvedValueOnce([{ id: 7 }] as never)
    await c.fetch(A)

    expect(c.read()).toEqual([{ id: 7 }])
  })
})
