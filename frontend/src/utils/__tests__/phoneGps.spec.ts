import { beforeEach, describe, expect, it } from 'vitest'
import type { PhonePosition } from '@/services/phonePositionsApi'
import {
  MAX_QUEUED,
  dropQueued,
  forgetQueuedPositions,
  phoneTrackerId,
  queuePosition,
  queuedPositions,
  toPhonePosition,
  worthKeeping,
} from '../phoneGps'

const VIN = 'FAKEVN00000000001'
const start = { time: 0, latitude: 52, longitude: 5 }
// About 11 m north of start.
const moved = (time: number) => ({ time, latitude: 52.0001, longitude: 5 })

function position(recordedAt: string): PhonePosition {
  return { recordedAt, latitude: 52, longitude: 5, accuracyM: 5, speedKmh: 40 }
}

beforeEach(() => localStorage.clear())

describe('worthKeeping', () => {
  it('keeps the first accurate fix and drops an inaccurate one', () => {
    expect(worthKeeping(null, start, 10)).toBe(true)
    expect(worthKeeping(null, start, 51)).toBe(false)
    expect(worthKeeping(null, start, Number.NaN)).toBe(false)
  })

  it('keeps a fix once the phone moved and five seconds passed', () => {
    expect(worthKeeping(start, moved(4_000), 10)).toBe(false)
    expect(worthKeeping(start, moved(5_000), 10)).toBe(true)
  })

  it('keeps one fix a minute while the phone stands still', () => {
    expect(worthKeeping(start, { ...start, time: 30_000 }, 10)).toBe(false)
    expect(worthKeeping(start, { ...start, time: 60_000 }, 10)).toBe(true)
  })
})

describe('toPhonePosition', () => {
  function fix(speed: number | null): GeolocationPosition {
    return {
      timestamp: Date.UTC(2026, 0, 1, 10),
      coords: { latitude: 52, longitude: 5, accuracy: 8, speed },
    } as GeolocationPosition
  }

  it('turns metres a second into km/h and keeps the moment', () => {
    expect(toPhonePosition(fix(10))).toEqual({
      recordedAt: '2026-01-01T10:00:00.000Z',
      latitude: 52,
      longitude: 5,
      accuracyM: 8,
      speedKmh: 36,
    })
  })

  it('leaves out a speed the phone does not know', () => {
    expect(toPhonePosition(fix(null)).speedKmh).toBeNull()
    expect(toPhonePosition(fix(-1)).speedKmh).toBeNull()
  })
})

describe('phoneTrackerId', () => {
  it('makes a random id once and keeps it', () => {
    const id = phoneTrackerId()
    expect(id).toMatch(/^[0-9a-f]{32}$/)
    expect(phoneTrackerId()).toBe(id)
  })

  it('replaces a stored id the API would refuse', () => {
    localStorage.setItem('garagestack-phone-tracker', 'bad id')
    expect(phoneTrackerId()).toMatch(/^[0-9a-f]{32}$/)
  })
})

describe('the queue', () => {
  it('keeps positions per car until they are sent', () => {
    queuePosition(VIN, position('2026-01-01T10:00:00.000Z'))
    queuePosition(VIN, position('2026-01-01T10:00:05.000Z'))
    queuePosition('FAKEVN00000000002', position('2026-01-01T10:00:00.000Z'))

    const sent = queuedPositions(VIN).slice(0, 1)
    // Recorded while the batch was on its way.
    queuePosition(VIN, position('2026-01-01T10:00:10.000Z'))
    dropQueued(VIN, sent)

    expect(queuedPositions(VIN).map((p) => p.recordedAt)).toEqual([
      '2026-01-01T10:00:05.000Z',
      '2026-01-01T10:00:10.000Z',
    ])
    expect(queuedPositions('FAKEVN00000000002')).toHaveLength(1)
  })

  it('drops the oldest once full', () => {
    const many = Array.from({ length: MAX_QUEUED }, (_, i) =>
      position(new Date(Date.UTC(2026, 0, 1) + i * 5_000).toISOString()),
    )
    localStorage.setItem('garagestack-phone-queue', JSON.stringify({ [VIN]: many }))

    queuePosition(VIN, position('2027-01-01T00:00:00.000Z'))

    const queued = queuedPositions(VIN)
    expect(queued).toHaveLength(MAX_QUEUED)
    expect(queued[0]!.recordedAt).toBe(many[1]!.recordedAt)
    expect(queued[queued.length - 1]!.recordedAt).toBe('2027-01-01T00:00:00.000Z')
  })

  it('reads past a damaged copy and is forgotten on sign-out', () => {
    localStorage.setItem('garagestack-phone-queue', '{"FAKEVN00000000001":[{"latitude":"x"}]}')
    expect(queuedPositions(VIN)).toEqual([])
    localStorage.setItem('garagestack-phone-queue', 'not json')
    expect(queuedPositions(VIN)).toEqual([])

    queuePosition(VIN, position('2026-01-01T10:00:00.000Z'))
    forgetQueuedPositions()
    expect(localStorage.getItem('garagestack-phone-queue')).toBeNull()
  })
})
