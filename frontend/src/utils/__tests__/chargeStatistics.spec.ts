import { describe, it, expect } from 'vitest'
import type { ChargeSession } from '@/services/vehicleApi'
import {
  chargeHabit,
  chargePairs,
  fullBatteryRangeKm,
  typicalDistanceBetweenCharges,
  type ChargePair,
} from '../chargeStatistics'

let day = 0

// A finished charge: it ended at `endSoc`, with the odometer at `odometerKm`, `day` days after the last.
function session(overrides: Partial<ChargeSession> = {}): ChargeSession {
  day += 1
  const date = `2026-10-${String(day).padStart(2, '0')}`
  return {
    startedAt: `${date}T20:00:00Z`,
    endedAt: `${date}T23:00:00Z`,
    startSocPercent: 30,
    endSocPercent: 90,
    chargingType: 'AC',
    odometerKm: 1000,
    latitude: null,
    longitude: null,
    startBatteryKwh: null,
    endBatteryKwh: null,
    batteryEnergyGainedKwh: null,
    maxPowerKw: 7,
    ...overrides,
  }
}

// Charges at these odometer readings, each one ending at 90 % and the next one starting at the level given.
function charges(...stops: [odometerKm: number, startSoc: number][]): ChargeSession[] {
  day = 0
  return stops.map(([odometerKm, startSoc]) =>
    session({ odometerKm, startSocPercent: startSoc, endSocPercent: 90 }),
  )
}

describe('chargePairs', () => {
  it('pairs each charge with the next, and says how far and how much battery it took', () => {
    const pairs = chargePairs(charges([1000, 30], [1180, 40], [1400, 25]))

    expect(pairs).toEqual([
      { distanceKm: 180, socUsedPercent: 50 },
      { distanceKm: 220, socUsedPercent: 65 },
    ])
  })

  it('reads the sessions in time order, whichever way the API sends them', () => {
    const newestFirst = charges([1000, 30], [1180, 40], [1400, 25]).reverse()

    expect(chargePairs(newestFirst)).toHaveLength(2)
    expect(chargePairs(newestFirst)[0]!.distanceKm).toBe(180)
  })

  it('starts no pair from a charge still going', () => {
    const list = charges([1000, 30], [1180, 40])
    list[0]!.endedAt = null

    expect(chargePairs(list)).toEqual([])
  })

  it('keeps the distance of a pair whose levels are missing, without the battery it took', () => {
    const list = charges([1000, 30], [1180, 40])
    list[0]!.endSocPercent = null

    expect(chargePairs(list)).toEqual([{ distanceKm: 180, socUsedPercent: null }])
  })

  it('does not take a level that went up between two charges for a drive', () => {
    const list = charges([1000, 30], [1180, 95])

    expect(chargePairs(list)).toEqual([{ distanceKm: 180, socUsedPercent: null }])
  })

  it('leaves out a pair with a missing odometer, or one that went backwards', () => {
    const noOdometer = charges([1000, 30], [1180, 40])
    noOdometer[1]!.odometerKm = null
    const backwards = charges([1000, 30], [900, 40])

    expect(chargePairs(noOdometer)).toEqual([])
    expect(chargePairs(backwards)).toEqual([])
  })

  it('has no pair with fewer than two charges', () => {
    expect(chargePairs([])).toEqual([])
    expect(chargePairs(charges([1000, 30]))).toEqual([])
  })
})

describe('typicalDistanceBetweenCharges', () => {
  const pair = (distanceKm: number): ChargePair => ({ distanceKm, socUsedPercent: 50 })

  it('is the middle of the distances', () => {
    expect(typicalDistanceBetweenCharges([pair(150), pair(200), pair(260)])).toBe(200)
  })

  it('is not pulled up by a charge nobody saw, which makes one long pair', () => {
    expect(
      typicalDistanceBetweenCharges([pair(150), pair(160), pair(170), pair(180), pair(520)]),
    ).toBe(170)
  })

  it('leaves out a split charge: a hop of a few kilometres is not a drive', () => {
    expect(typicalDistanceBetweenCharges([pair(0), pair(1), pair(150), pair(200), pair(260)])).toBe(
      200,
    )
  })

  it('is null with fewer than three pairs', () => {
    expect(typicalDistanceBetweenCharges([pair(150), pair(200)])).toBeNull()
  })
})

describe('fullBatteryRangeKm', () => {
  const pair = (distanceKm: number, socUsedPercent: number | null): ChargePair => ({
    distanceKm,
    socUsedPercent,
  })

  it('is the distance per percent of battery, times 100, added up over the pairs', () => {
    // 150 km on 50 %, 180 km on 60 %, 120 km on 40 %: 3 km per percent, so 300 km on a full battery.
    expect(fullBatteryRangeKm([pair(150, 50), pair(180, 60), pair(120, 40)])).toBeCloseTo(300)
  })

  it('counts a long stretch for more than a short one', () => {
    // 400 km on 100 % (4 km per percent), 20 km on 10 % (2), 80 km on 20 % (4).
    expect(fullBatteryRangeKm([pair(400, 100), pair(20, 10), pair(80, 20)])).toBeCloseTo(
      (500 / 130) * 100,
    )
  })

  it('leaves out a pair far above the rest, which spans a charge nobody saw', () => {
    // The 400 km on 20 % pair is 20 km per percent against 3 for the others.
    expect(
      fullBatteryRangeKm([pair(150, 50), pair(180, 60), pair(120, 40), pair(400, 20)]),
    ).toBeCloseTo(300)
  })

  it('ignores a pair too short to read, or without its battery', () => {
    expect(
      fullBatteryRangeKm([
        pair(150, 50),
        pair(180, 60),
        pair(120, 40),
        pair(5, 20),
        pair(50, 4),
        pair(100, null),
      ]),
    ).toBeCloseTo(300)
  })

  it('is null with fewer than three pairs to go on', () => {
    expect(fullBatteryRangeKm([pair(150, 50), pair(180, 60)])).toBeNull()
  })

  it('is null when too many pairs are far off the middle to keep three', () => {
    expect(fullBatteryRangeKm([pair(100, 50), pair(400, 50), pair(900, 50)])).toBeNull()
  })
})

describe('chargeHabit', () => {
  it('is the average level charges start from and end at', () => {
    day = 0
    const list = [
      session({ startSocPercent: 20, endSocPercent: 80 }),
      session({ startSocPercent: 30, endSocPercent: 90 }),
      session({ startSocPercent: 40, endSocPercent: 100 }),
    ]

    expect(chargeHabit(list)).toEqual({ startPercent: 30, endPercent: 90 })
  })

  it('leaves out a charge still going and one without its levels', () => {
    day = 0
    const list = [
      session({ startSocPercent: 20, endSocPercent: 80 }),
      session({ startSocPercent: 30, endSocPercent: 90 }),
      session({ startSocPercent: 40, endSocPercent: 100 }),
      session({ startSocPercent: 50, endSocPercent: 55, endedAt: null }),
      session({ startSocPercent: null, endSocPercent: null }),
    ]

    expect(chargeHabit(list)).toEqual({ startPercent: 30, endPercent: 90 })
  })

  it('is null with fewer than three charges to go on', () => {
    day = 0
    expect(chargeHabit([session(), session()])).toBeNull()
  })
})
