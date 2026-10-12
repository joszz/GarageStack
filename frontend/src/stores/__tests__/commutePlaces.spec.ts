import { describe, it, expect, beforeEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useCommutePlacesStore } from '@/stores/commutePlaces'
import { commutePlaceAt } from '@/utils/commutePlaces'
import type { CommutePlace, CommutePlaceChange } from '@/services/commutePlacesApi'

vi.mock('@/services/commutePlacesApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/services/commutePlacesApi')>()),
  commutePlacesApi: {
    list: vi.fn<() => Promise<CommutePlace[]>>().mockResolvedValue([]),
    create: vi.fn<() => Promise<CommutePlaceChange>>(),
    update: vi.fn<() => Promise<CommutePlaceChange>>(),
    remove: vi.fn<() => Promise<CommutePlaceChange>>(),
  },
}))

const home: CommutePlace = {
  id: 1,
  kind: 'home',
  label: null,
  latitude: 52.0,
  longitude: 5.0,
  radiusMeters: 250,
}
const work: CommutePlace = { ...home, id: 2, kind: 'work', label: 'Office', latitude: 52.1 }

describe('useCommutePlacesStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('saving a place reports what it re-labelled and reloads the list in the API order', async () => {
    const { commutePlacesApi } = await import('@/services/commutePlacesApi')
    vi.mocked(commutePlacesApi.create).mockResolvedValue({ place: work, marked: 3, cleared: 0 })
    vi.mocked(commutePlacesApi.list).mockResolvedValue([home, work])
    const store = useCommutePlacesStore()

    const change = await store.savePlace('VIN1', { ...work })

    expect(change).toEqual({ place: work, marked: 3, cleared: 0 })
    expect(store.places).toEqual([home, work])
  })

  it('updating sends the place to its own address', async () => {
    const { commutePlacesApi } = await import('@/services/commutePlacesApi')
    vi.mocked(commutePlacesApi.update).mockResolvedValue({ place: home, marked: 0, cleared: 0 })
    const store = useCommutePlacesStore()

    await store.savePlace('VIN1', { ...home }, 1)

    expect(commutePlacesApi.update).toHaveBeenCalledWith('VIN1', 1, { ...home })
    expect(commutePlacesApi.create).not.toHaveBeenCalled()
  })

  it('a refused save resolves to null with the error kept', async () => {
    const { commutePlacesApi } = await import('@/services/commutePlacesApi')
    vi.mocked(commutePlacesApi.create).mockRejectedValue(new Error('refused'))
    const store = useCommutePlacesStore()

    expect(await store.savePlace('VIN1', { ...home })).toBeNull()
    expect(store.actionError?.message).toBe('refused')
  })

  it('removing a place drops it from the list', async () => {
    const { commutePlacesApi } = await import('@/services/commutePlacesApi')
    vi.mocked(commutePlacesApi.list).mockResolvedValue([home, work])
    vi.mocked(commutePlacesApi.remove).mockResolvedValue({ place: null, marked: 0, cleared: 2 })
    const store = useCommutePlacesStore()
    await store.fetchPlaces('VIN1')

    const change = await store.removePlace('VIN1', 2)

    expect(change?.cleared).toBe(2)
    expect(store.places).toEqual([home])
  })
})

describe('commutePlaceAt', () => {
  // About 111 m per thousandth of a degree of latitude.
  it('finds the place a trip end is within', () => {
    expect(commutePlaceAt([home, work], 52.002, 5.0)).toBe(home)
    expect(commutePlaceAt([home, work], 52.003, 5.0)).toBeNull()
  })

  it('takes the nearest when places overlap', () => {
    const nextDoor = { ...work, latitude: 52.003 }

    expect(commutePlaceAt([home, nextDoor], 52.0025, 5.0)).toBe(nextDoor)
  })
})
