import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { nextTick } from 'vue'
import type { GeocodePrecision, GeoPoint, Place, ReverseGeocodeResponse } from '@/services/mapApi'

const mockReverseGeocode =
  vi.fn<
    (
      points: GeoPoint[],
      precision: GeocodePrecision,
      language: string,
    ) => Promise<ReverseGeocodeResponse>
  >()

// The factory runs when the composable is first imported inside a test, which is after this
// module's own initialisation, so referencing the mock here is safe.
// Only the requests are faked: the batch size and the retry pacing stay the real ones.
vi.mock('@/services/mapApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/services/mapApi')>()),
  mapApi: { reverseGeocode: mockReverseGeocode },
}))

function city(name: string): Place {
  return {
    road: null,
    houseNumber: null,
    city: name,
    postcode: null,
    countryCode: 'nl',
    displayName: `${name}, Nederland`,
  }
}

function answer(results: (Place | null)[], overrides: Partial<ReverseGeocodeResponse> = {}) {
  return {
    results,
    available: true,
    hasMore: results.some((r) => r === null),
    ...overrides,
  }
}

const zwolle = { lat: 52.5123, lng: 6.0921 }
const deventer = { lat: 52.2554, lng: 6.1639 }

async function load() {
  const { useReverseGeocode } = await import('@/composables/useReverseGeocode')
  return useReverseGeocode()
}

describe('useReverseGeocode', () => {
  beforeEach(() => {
    vi.resetModules()
    setActivePinia(createPinia())
    localStorage.clear()
    mockReverseGeocode.mockReset()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('resolves queued coordinates in one batched request', async () => {
    mockReverseGeocode.mockResolvedValue(answer([city('Zwolle'), city('Deventer')]))
    const { requestPlaces, placeFor } = await load()

    requestPlaces([zwolle, deventer], 'city')
    await vi.waitFor(() => expect(mockReverseGeocode).toHaveBeenCalledOnce())
    await nextTick()

    const [points, precision, language] = mockReverseGeocode.mock.calls[0]!
    expect(points).toEqual([zwolle, deventer])
    expect(precision).toBe('city')
    expect(language).toBe('en')
    expect(placeFor(zwolle.lat, zwolle.lng, 'city')?.city).toBe('Zwolle')
    expect(placeFor(deventer.lat, deventer.lng, 'city')?.city).toBe('Deventer')
  })

  it('never asks twice about the same coordinate', async () => {
    mockReverseGeocode.mockResolvedValue(answer([city('Zwolle')]))
    const { requestPlaces } = await load()

    requestPlaces([zwolle, { ...zwolle }], 'city')
    await vi.waitFor(() => expect(mockReverseGeocode).toHaveBeenCalledOnce())
    await nextTick()

    // Once for the duplicate inside the first call, and not at all for the repeat request.
    expect(mockReverseGeocode.mock.calls[0]![0]).toEqual([zwolle])
    requestPlaces([zwolle], 'city')
    await nextTick()
    expect(mockReverseGeocode).toHaveBeenCalledOnce()
  })

  it('keeps the two precisions apart', async () => {
    mockReverseGeocode.mockResolvedValue(answer([city('Zwolle')]))
    const { requestPlaces, placeFor } = await load()

    requestPlaces([zwolle], 'city')
    await vi.waitFor(() => expect(mockReverseGeocode).toHaveBeenCalledOnce())
    await nextTick()

    expect(placeFor(zwolle.lat, zwolle.lng, 'address')).toBeNull()
    requestPlaces([zwolle], 'address')
    await vi.waitFor(() => expect(mockReverseGeocode).toHaveBeenCalledTimes(2))
    expect(mockReverseGeocode.mock.calls[1]![1]).toBe('address')
  })

  it('stops asking once the server reports geocoding is switched off', async () => {
    mockReverseGeocode.mockResolvedValue(answer([null], { available: false, hasMore: false }))
    const { requestPlaces, placeNamesEnabled } = await load()

    requestPlaces([zwolle], 'city')
    await vi.waitFor(() => expect(mockReverseGeocode).toHaveBeenCalledOnce())
    await nextTick()

    expect(placeNamesEnabled.value).toBe(false)

    requestPlaces([deventer], 'city')
    await nextTick()
    expect(mockReverseGeocode).toHaveBeenCalledOnce()
  })

  it('asks again for points the server left unresolved, then gives up', async () => {
    vi.useFakeTimers()
    mockReverseGeocode.mockResolvedValue(answer([null]))
    const { requestPlaces, placeFor, placesResolving } = await load()

    requestPlaces([zwolle], 'city')

    // Four rounds, each after the pump's pause, and then nothing further.
    for (let round = 0; round < 6; round++) await vi.advanceTimersByTimeAsync(1000)

    expect(mockReverseGeocode).toHaveBeenCalledTimes(4)
    expect(placeFor(zwolle.lat, zwolle.lng, 'city')).toBeNull()
    expect(placesResolving.value).toBe(false)
  })

  it('keeps asking while the server resolves part of a batch, past the give-up threshold', async () => {
    vi.useFakeTimers()
    // The server works through its own upstream budget, answering the first of whatever is still
    // unresolved: five coordinates therefore need more rounds than the give-up threshold allows
    // for a stuck point, and none of them may be abandoned along the way.
    mockReverseGeocode.mockImplementation((points) =>
      Promise.resolve(answer(points.map((_, index) => (index === 0 ? city('Somewhere') : null)))),
    )
    const spread = [zwolle, deventer, { lat: 51.9, lng: 4.5 }, { lat: 53.2, lng: 6.5 }]
    const { requestPlaces, placeFor } = await load()

    requestPlaces(spread, 'city')
    for (let round = 0; round < 8; round++) await vi.advanceTimersByTimeAsync(1000)

    expect(mockReverseGeocode).toHaveBeenCalledTimes(spread.length)
    for (const point of spread) {
      expect(placeFor(point.lat, point.lng, 'city')?.city).toBe('Somewhere')
    }
  })

  it('lets the two precisions take turns so neither starves', async () => {
    vi.useFakeTimers()
    mockReverseGeocode.mockResolvedValue(answer([null]))
    const { requestPlaces } = await load()

    requestPlaces([zwolle, deventer, { lat: 51.9, lng: 4.5 }], 'city')
    requestPlaces([zwolle], 'address')
    await vi.advanceTimersByTimeAsync(2000)

    expect(mockReverseGeocode.mock.calls[0]![1]).toBe('city')
    expect(mockReverseGeocode.mock.calls[1]![1]).toBe('address')
  })

  it('survives a failing request without caching anything', async () => {
    mockReverseGeocode.mockRejectedValue(new Error('offline'))
    const { requestPlaces, placeFor, placesResolving } = await load()

    requestPlaces([zwolle], 'city')
    await vi.waitFor(() => expect(placesResolving.value).toBe(false))

    expect(placeFor(zwolle.lat, zwolle.lng, 'city')).toBeNull()
  })

  it('asks for nothing while the place-names setting is off, and hides what it already has', async () => {
    mockReverseGeocode.mockResolvedValue(answer([city('Zwolle')]))
    const { requestPlaces, placeFor, placeNamesEnabled } = await load()
    const { useUiSettingsStore } = await import('@/stores/settingsUi')
    const uiSettings = useUiSettingsStore()

    requestPlaces([zwolle], 'city')
    await vi.waitFor(() => expect(mockReverseGeocode).toHaveBeenCalledOnce())
    await nextTick()
    expect(placeFor(zwolle.lat, zwolle.lng, 'city')?.city).toBe('Zwolle')

    uiSettings.placeNamesEnabled = false
    await nextTick()

    expect(placeNamesEnabled.value).toBe(false)
    expect(placeFor(zwolle.lat, zwolle.lng, 'city')).toBeNull()

    requestPlaces([deventer], 'city')
    await nextTick()
    expect(mockReverseGeocode).toHaveBeenCalledOnce()
  })

  it('asks again for what is on screen when the setting is turned back on', async () => {
    mockReverseGeocode.mockResolvedValue(answer([city('Deventer')]))
    const { useUiSettingsStore } = await import('@/stores/settingsUi')
    const uiSettings = useUiSettingsStore()
    uiSettings.placeNamesEnabled = false

    const { requestPlaces, placeFor } = await load()

    // Browsing while the setting is off must still record what was on screen.
    requestPlaces([deventer], 'city')
    await nextTick()
    expect(mockReverseGeocode).not.toHaveBeenCalled()

    uiSettings.placeNamesEnabled = true
    await vi.waitFor(() => expect(mockReverseGeocode).toHaveBeenCalledOnce())
    await nextTick()

    expect(placeFor(deventer.lat, deventer.lng, 'city')?.city).toBe('Deventer')
  })

  it('looks the same places up again in the new language when the locale changes', async () => {
    mockReverseGeocode.mockResolvedValue(answer([city('Den Haag')]))
    const { requestPlaces, placeFor } = await load()
    const { useUiSettingsStore } = await import('@/stores/settingsUi')
    const uiSettings = useUiSettingsStore()

    requestPlaces([zwolle], 'city')
    await vi.waitFor(() => expect(mockReverseGeocode).toHaveBeenCalledOnce())
    await nextTick()

    uiSettings.locale = 'nl'
    await nextTick()
    await vi.waitFor(() => expect(mockReverseGeocode).toHaveBeenCalledTimes(2))
    await nextTick()

    expect(mockReverseGeocode.mock.calls[1]![2]).toBe('nl')
    expect(placeFor(zwolle.lat, zwolle.lng, 'city')?.city).toBe('Den Haag')
  })
})
