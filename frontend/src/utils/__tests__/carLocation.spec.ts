import { describe, it, expect } from 'vitest'
import { carLocation } from '../carLocation'

const car = { id: 1 }
const status = {
  vehicleId: 1,
  latitude: 52.09,
  longitude: 5.12,
  speed: 0,
  engineRunning: false,
  currentJourneyDistance: 0,
  lastVehicleStateAt: '2026-10-19T10:00:00Z',
}

describe('carLocation', () => {
  it('only uses a status belonging to the selected vehicle', () => {
    expect(carLocation(null, status)).toBeNull()
    expect(carLocation(car, null)).toBeNull()
    expect(carLocation({ id: 2 }, status)).toBeNull()
    expect(carLocation(car, status)).toEqual({
      vehicleId: 1,
      latitude: 52.09,
      longitude: 5.12,
      mayBeMoving: false,
      vehicleUpdatedAt: status.lastVehicleStateAt,
    })
  })

  it.each([
    [null, 5],
    [52, null],
    [NaN, 5],
    [52, Infinity],
    [-Infinity, 5],
    [-90.001, 5],
    [90.001, 5],
    [52, -180.001],
    [52, 180.001],
  ])('rejects invalid coordinates %s, %s', (latitude, longitude) => {
    expect(carLocation(car, { ...status, latitude, longitude })).toBeNull()
  })

  it.each([
    [0, 0],
    [-90, -180],
    [90, 180],
  ])('accepts valid coordinates %s, %s', (latitude, longitude) => {
    expect(carLocation(car, { ...status, latitude, longitude })).toMatchObject({
      latitude,
      longitude,
    })
  })

  it('treats an active journey or a reported speed as possible movement', () => {
    expect(carLocation(car, { ...status, currentJourneyDistance: 0.1 })?.mayBeMoving).toBe(true)
    expect(carLocation(car, { ...status, engineRunning: true, speed: 1 })?.mayBeMoving).toBe(true)
    expect(carLocation(car, { ...status, engineRunning: null, speed: 1 })?.mayBeMoving).toBe(true)
  })

  it('does not mistake a retained old speed for motion after the engine reports off', () => {
    expect(carLocation(car, { ...status, speed: 35 })?.mayBeMoving).toBe(false)
    expect(carLocation(car, { ...status, engineRunning: null, speed: 0 })?.mayBeMoving).toBe(false)
    expect(
      carLocation(car, {
        ...status,
        engineRunning: null,
        speed: NaN,
        currentJourneyDistance: Infinity,
      })?.mayBeMoving,
    ).toBe(false)
  })

  it('does not turn an invalid vehicle update time into a GPS-fix time', () => {
    expect(
      carLocation(car, { ...status, lastVehicleStateAt: 'not a date' })?.vehicleUpdatedAt,
    ).toBe(null)
    expect(carLocation(car, { ...status, lastVehicleStateAt: null })?.vehicleUpdatedAt).toBe(null)
  })
})
