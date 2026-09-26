import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useTripLogStore } from '@/stores/tripLog'
import { tripLogApi, type TripPlacesResult } from '@/services/tripLogApi'
import { GEOCODE_MAX_ATTEMPTS } from '@/services/mapApi'
import { deventer, logEntry, place, zwolle } from '@/services/__tests__/tripLogFixtures'

vi.mock('@/services/tripLogApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/services/tripLogApi')>()
  return {
    ...actual,
    tripLogApi: {
      log: vi.fn<typeof actual.tripLogApi.log>(),
      update: vi.fn<typeof actual.tripLogApi.update>(),
      setPurpose: vi.fn<typeof actual.tripLogApi.setPurpose>(),
      resolvePlaces: vi.fn<typeof actual.tripLogApi.resolvePlaces>(),
    },
  }
})

const api = vi.mocked(tripLogApi)

function answered(trips: TripPlacesResult['trips'], hasMore = false): TripPlacesResult {
  return { trips, available: true, hasMore }
}

describe('useTripLogStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('loads the period from its first local midnight up to the next', async () => {
    api.log.mockResolvedValue([logEntry()])
    const store = useTripLogStore()

    await store.fetchLog('VIN1', { year: 2026, month: 8 })

    expect(api.log).toHaveBeenCalledWith(
      'VIN1',
      new Date(2026, 8, 1).toISOString(),
      new Date(2026, 9, 1).toISOString(),
    )
    expect(store.entries).toHaveLength(1)
    expect(store.totals.unclassified.trips).toBe(1)
  })

  it('keeps the newest load when an older one answers last', async () => {
    let answerSeptember: (value: ReturnType<typeof logEntry>[]) => void = () => {}
    api.log
      .mockReturnValueOnce(new Promise((resolve) => (answerSeptember = resolve)))
      .mockResolvedValueOnce([logEntry({ id: 10 })])
    const store = useTripLogStore()

    const september = store.fetchLog('VIN1', { year: 2026, month: 8 })
    await store.fetchLog('VIN1', { year: 2026, month: 9 })
    answerSeptember([logEntry({ id: 9 })])
    await september

    expect(store.entries.map((e) => e.id)).toEqual([10])
  })

  it('reports a failed load', async () => {
    api.log.mockRejectedValue(new Error('offline'))
    const store = useTripLogStore()

    await store.fetchLog('VIN1', { year: 2026, month: 8 })

    expect(store.loadError?.message).toContain('offline')
  })

  it('replaces a trip with the saved version, keeping places the page already has', async () => {
    api.log.mockResolvedValue([logEntry({ endPlace: place() })])
    api.update.mockResolvedValue(logEntry({ purpose: 'business', notes: 'Client visit' }))
    const store = useTripLogStore()
    await store.fetchLog('VIN1', { year: 2026, month: 8 })

    const ok = await store.updateEntry('VIN1', 1, 'business', 'Client visit')

    expect(ok).toBe(true)
    expect(api.update).toHaveBeenCalledWith('VIN1', 1, 'business', 'Client visit')
    expect(store.entries[0]).toMatchObject({ purpose: 'business', notes: 'Client visit' })
    expect(store.entries[0]!.endPlace).toEqual(place())
    expect(store.saving.size).toBe(0)
  })

  it('leaves a trip as it was when saving fails', async () => {
    api.log.mockResolvedValue([logEntry()])
    api.update.mockRejectedValue(new Error('500'))
    const store = useTripLogStore()
    await store.fetchLog('VIN1', { year: 2026, month: 8 })

    const ok = await store.updateEntry('VIN1', 1, 'private', null)

    expect(ok).toBe(false)
    expect(store.entries[0]!.purpose).toBeNull()
    expect(store.actionError?.message).toContain('500')
  })

  it('classifies only the trips without a purpose', async () => {
    api.log.mockResolvedValue([
      logEntry({ id: 1 }),
      logEntry({ id: 2, purpose: 'business' }),
      logEntry({ id: 3 }),
    ])
    api.setPurpose.mockResolvedValue({ changed: 2 })
    const store = useTripLogStore()
    await store.fetchLog('VIN1', { year: 2026, month: 8 })

    await store.classifyUnclassified('VIN1', 'commute')

    expect(api.setPurpose).toHaveBeenCalledWith('VIN1', [1, 3], 'commute')
    expect(store.entries.map((e) => e.purpose)).toEqual(['commute', 'business', 'commute'])
  })

  describe('resolvePlaces', () => {
    it('asks for the trips still missing a place, and fills them in', async () => {
      api.log.mockResolvedValue([
        logEntry({ id: 1 }),
        logEntry({ id: 2, startPlace: zwolle, endPlace: deventer }),
      ])
      api.resolvePlaces.mockResolvedValue(
        answered([{ id: 1, startPlace: zwolle, endPlace: deventer }]),
      )
      const store = useTripLogStore()
      await store.fetchLog('VIN1', { year: 2026, month: 8 })

      await store.resolvePlaces('VIN1', 'nl')

      expect(api.resolvePlaces).toHaveBeenCalledExactlyOnceWith('VIN1', [1], 'nl')
      expect(store.entries[0]).toMatchObject({ startPlace: zwolle, endPlace: deventer })
      expect(store.placesResolving).toBe(false)
    })

    it('comes back for the rest over several rounds', async () => {
      api.log.mockResolvedValue([logEntry({ id: 1 }), logEntry({ id: 2 })])
      api.resolvePlaces
        .mockResolvedValueOnce(
          answered(
            [
              { id: 1, startPlace: zwolle, endPlace: deventer },
              { id: 2, startPlace: null, endPlace: null },
            ],
            true,
          ),
        )
        .mockResolvedValueOnce(answered([{ id: 2, startPlace: deventer, endPlace: zwolle }]))
      const store = useTripLogStore()
      await store.fetchLog('VIN1', { year: 2026, month: 8 })

      const run = store.resolvePlaces('VIN1', 'nl')
      await vi.runAllTimersAsync()
      await run

      expect(api.resolvePlaces).toHaveBeenLastCalledWith('VIN1', [2], 'nl')
      expect(store.entries.every((e) => e.startPlace && e.endPlace)).toBe(true)
    })

    it('gives up on a trip after rounds that resolved nothing at all', async () => {
      api.log.mockResolvedValue([logEntry({ id: 1 })])
      api.resolvePlaces.mockResolvedValue(
        answered([{ id: 1, startPlace: null, endPlace: null }], true),
      )
      const store = useTripLogStore()
      await store.fetchLog('VIN1', { year: 2026, month: 8 })

      const run = store.resolvePlaces('VIN1', 'nl')
      await vi.runAllTimersAsync()
      await run

      expect(api.resolvePlaces).toHaveBeenCalledTimes(GEOCODE_MAX_ATTEMPTS)
    })

    it('stops asking once the deployment says geocoding is off', async () => {
      api.log.mockResolvedValue([logEntry({ id: 1 })])
      api.resolvePlaces.mockResolvedValue({ trips: [], available: false, hasMore: false })
      const store = useTripLogStore()
      await store.fetchLog('VIN1', { year: 2026, month: 8 })

      await store.resolvePlaces('VIN1', 'nl')
      await store.resolvePlaces('VIN1', 'nl')

      expect(api.resolvePlaces).toHaveBeenCalledOnce()
      expect(store.placesAvailable).toBe(false)
    })

    it('stops after the current round when told to', async () => {
      api.log.mockResolvedValue([logEntry({ id: 1 }), logEntry({ id: 2 })])
      api.resolvePlaces.mockResolvedValue(
        answered([{ id: 1, startPlace: zwolle, endPlace: deventer }], true),
      )
      const store = useTripLogStore()
      await store.fetchLog('VIN1', { year: 2026, month: 8 })

      const run = store.resolvePlaces('VIN1', 'nl')
      store.stopResolvingPlaces()
      await vi.runAllTimersAsync()
      await run

      expect(api.resolvePlaces).toHaveBeenCalledOnce()
    })

    it('carries on when asked again while a stopped run is finishing its round', async () => {
      api.log.mockResolvedValue([logEntry({ id: 1 }), logEntry({ id: 2 })])
      api.resolvePlaces
        .mockResolvedValueOnce(answered([{ id: 1, startPlace: zwolle, endPlace: deventer }], true))
        .mockResolvedValueOnce(answered([{ id: 2, startPlace: zwolle, endPlace: deventer }]))
      const store = useTripLogStore()
      await store.fetchLog('VIN1', { year: 2026, month: 8 })

      const run = store.resolvePlaces('VIN1', 'nl')
      store.stopResolvingPlaces()
      void store.resolvePlaces('VIN1', 'nl')
      await vi.runAllTimersAsync()
      await run

      expect(api.resolvePlaces).toHaveBeenCalledTimes(2)
      expect(store.entries[1]!.startPlace).toEqual(zwolle)
    })
  })
})
