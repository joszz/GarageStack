import { afterEach, describe, expect, it, vi } from 'vitest'
import type { TelemetrySnapshot, Trip, Vehicle } from '@/services/vehicleApi'
import {
  forgetLastKnownState,
  readLastKnownState,
  saveLastKnownState,
} from '@/utils/lastKnownState'

const STORAGE_KEY = 'garagestack-last-known'

const vehicle = { id: 1, vin: 'FAKEVN00000000001', model: 'MG' } as Vehicle
const status = {
  vehicleId: 1,
  recordedAt: '2026-11-18T10:00:00Z',
  latitude: 52.09,
} as TelemetrySnapshot
const trip = {
  id: 7,
  distanceKm: 12.3,
  points: [{ recordedAt: '2026-11-18T09:00:00Z', latitude: 52, longitude: 5, speed: 30 }],
} as Trip

afterEach(() => {
  localStorage.clear()
  vi.restoreAllMocks()
})

describe('lastKnownState', () => {
  it('reads back what was saved, stamped with when', () => {
    saveLastKnownState(
      { vehicles: [vehicle], status, config: { BType: '1' }, latestTrip: null },
      new Date('2026-11-18T10:05:00Z'),
    )

    expect(readLastKnownState()).toEqual({
      savedAt: '2026-11-18T10:05:00.000Z',
      vehicles: [vehicle],
      status,
      config: { BType: '1' },
      latestTrip: null,
    })
  })

  it("keeps the newest trip's figures but not its fixes", () => {
    saveLastKnownState({ vehicles: [vehicle], status, config: {}, latestTrip: trip })

    expect(readLastKnownState()?.latestTrip).toEqual({ ...trip, points: [] })
  })

  it('has nothing before anything was saved, and after it was forgotten', () => {
    expect(readLastKnownState()).toBeNull()

    saveLastKnownState({ vehicles: [vehicle], status, config: {}, latestTrip: null })
    forgetLastKnownState()

    expect(readLastKnownState()).toBeNull()
  })

  it.each([
    ['not JSON', '{'],
    ['from another build', JSON.stringify({ version: 0, savedAt: 'x', vehicles: [], config: {} })],
    ['without a vehicle list', JSON.stringify({ version: 1, savedAt: 'x', config: {} })],
    ['not an object', '42'],
  ])('ignores a saved copy that is %s', (_, raw) => {
    localStorage.setItem(STORAGE_KEY, raw)
    expect(readLastKnownState()).toBeNull()
  })

  it('carries on when the browser refuses to store it', () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('full', 'QuotaExceededError')
    })

    expect(() =>
      saveLastKnownState({ vehicles: [vehicle], status, config: {}, latestTrip: null }),
    ).not.toThrow()
  })
})
