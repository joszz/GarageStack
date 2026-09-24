import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { nextTick } from 'vue'
import type { GeoPoint, MapMatchResponse } from '@/services/mapApi'
import type { Trip } from '@/services/vehicleApi'

const mockMatchTrip = vi.fn<(points: GeoPoint[]) => Promise<MapMatchResponse>>()

// The factory runs when the composable is first imported inside a test, which is after this
// module's own initialisation, so referencing the mock here is safe.
vi.mock('@/services/mapApi', () => ({
  mapApi: { matchTrip: mockMatchTrip },
  MAX_MATCH_POINTS_PER_REQUEST: 600,
}))

// Two vertices a kilometre apart, as the API encodes them (six decimals).
const shape = 'qdbdcBqbzrJ_ibE_ibE'

function trip(points: number, startedAt = '2026-09-20T08:00:00Z'): Trip {
  return {
    index: 0,
    startedAt,
    endedAt: '2026-09-20T08:30:00Z',
    distanceKm: 12.3,
    pointCount: points,
    points: Array.from({ length: points }, (_, i) => ({
      recordedAt: new Date(Date.parse(startedAt) + i * 60_000).toISOString(),
      latitude: 52.5 + i * 0.001,
      longitude: 6.09 + i * 0.001,
      speed: 50 + i,
    })),
  }
}

function matched(overrides: Partial<MapMatchResponse> = {}): MapMatchResponse {
  return {
    available: true,
    matched: true,
    pending: false,
    shape,
    pointIndexes: [0, 1, 1],
    matchedKm: 13.1,
    ...overrides,
  }
}

async function load() {
  const { useTripMatch } = await import('@/composables/useTripMatch')
  return useTripMatch()
}

describe('useTripMatch', () => {
  beforeEach(() => {
    vi.resetModules()
    setActivePinia(createPinia())
    localStorage.clear()
    mockMatchTrip.mockReset()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it("sends a trip's fixes and hands back the decoded line", async () => {
    mockMatchTrip.mockResolvedValue(matched())
    const { requestMatch, matchFor } = await load()
    const ride = trip(3)

    requestMatch(ride)
    await vi.waitFor(() => expect(mockMatchTrip).toHaveBeenCalledOnce())
    await nextTick()

    expect(mockMatchTrip.mock.calls[0]![0]).toEqual([
      { lat: 52.5, lng: 6.09 },
      { lat: 52.501, lng: 6.091 },
      { lat: 52.502, lng: 6.092 },
    ])

    const match = matchFor(ride)!
    expect(match.coordinates).toHaveLength(2)
    expect(match.coordinates[0]![0]).toBeCloseTo(52.512345, 6)
    expect(match.pointIndexes).toEqual([0, 1, 1])
    expect(match.points).toHaveLength(3)
    expect(match.matchedKm).toBe(13.1)
  })

  it('asks about a trip once and serves the rest from its cache', async () => {
    mockMatchTrip.mockResolvedValue(matched())
    const { requestMatch, matchFor } = await load()
    const ride = trip(3)

    requestMatch(ride)
    await vi.waitFor(() => expect(mockMatchTrip).toHaveBeenCalledOnce())
    await nextTick()

    requestMatch(ride)
    await nextTick()

    expect(mockMatchTrip).toHaveBeenCalledOnce()
    expect(matchFor(ride)).not.toBeNull()
  })

  it('treats a trip that has gained fixes as a new trace', async () => {
    mockMatchTrip.mockResolvedValue(matched())
    const { requestMatch } = await load()

    requestMatch(trip(3))
    await vi.waitFor(() => expect(mockMatchTrip).toHaveBeenCalledOnce())
    await nextTick()

    // The trip being driven grew by a fix while it was on screen.
    requestMatch(trip(4))
    await vi.waitFor(() => expect(mockMatchTrip).toHaveBeenCalledTimes(2))
  })

  it('gives up on a trip the matcher recognises no road for', async () => {
    mockMatchTrip.mockResolvedValue(matched({ matched: false, shape: null, pointIndexes: null }))
    const { requestMatch, matchFor } = await load()
    const ride = trip(3)

    requestMatch(ride)
    await vi.waitFor(() => expect(mockMatchTrip).toHaveBeenCalledOnce())
    await nextTick()

    expect(matchFor(ride)).toBeNull()

    requestMatch(ride)
    await nextTick()
    expect(mockMatchTrip).toHaveBeenCalledOnce()
  })

  it('retries while the matcher asks it to come back later', async () => {
    vi.useFakeTimers()
    mockMatchTrip
      .mockResolvedValueOnce(
        matched({ matched: false, pending: true, shape: null, pointIndexes: null }),
      )
      .mockResolvedValue(matched())
    const { requestMatch, matchFor } = await load()
    const ride = trip(3)

    requestMatch(ride)
    await vi.waitFor(() => expect(mockMatchTrip).toHaveBeenCalledOnce())
    await vi.advanceTimersByTimeAsync(1500)

    await vi.waitFor(() => expect(mockMatchTrip).toHaveBeenCalledTimes(2))
    expect(matchFor(ride)).not.toBeNull()
  })

  it('stops asking once the server reports matching is switched off', async () => {
    mockMatchTrip.mockResolvedValue(
      matched({ available: false, matched: false, shape: null, pointIndexes: null }),
    )
    const { requestMatch, snapEnabled } = await load()

    requestMatch(trip(3))
    await vi.waitFor(() => expect(mockMatchTrip).toHaveBeenCalledOnce())
    await nextTick()

    expect(snapEnabled.value).toBe(false)

    requestMatch(trip(5))
    await nextTick()
    expect(mockMatchTrip).toHaveBeenCalledOnce()
  })

  it('hides snapped lines while the setting is off, and shows them again without asking', async () => {
    mockMatchTrip.mockResolvedValue(matched())
    const { useMapSettingsStore } = await import('@/stores/settingsMap')
    const settings = useMapSettingsStore()
    const { requestMatch, matchFor } = await load()
    const ride = trip(3)

    requestMatch(ride)
    await vi.waitFor(() => expect(mockMatchTrip).toHaveBeenCalledOnce())
    await nextTick()

    settings.snapToRoadsEnabled = false
    await nextTick()
    expect(matchFor(ride)).toBeNull()

    settings.snapToRoadsEnabled = true
    await nextTick()
    expect(matchFor(ride)).not.toBeNull()
    expect(mockMatchTrip).toHaveBeenCalledOnce()
  })

  it('leaves a trip with a single fix alone', async () => {
    const { requestMatch } = await load()

    requestMatch(trip(1))
    await nextTick()

    expect(mockMatchTrip).not.toHaveBeenCalled()
  })
})
