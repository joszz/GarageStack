import { describe, expect, it } from 'vitest'
import type { ChargeSession } from '@/services/vehicleApi'
import { energyGainedKwh, sincePreviousCharge } from '../chargeSessions'
import { trustedBatteryKwh } from '../energy'

function session(fields: Partial<ChargeSession>): ChargeSession {
  return {
    startedAt: '2026-10-20T08:00:00Z',
    endedAt: '2026-10-20T10:00:00Z',
    startSocPercent: null,
    endSocPercent: null,
    chargingType: null,
    odometerKm: null,
    latitude: null,
    longitude: null,
    startBatteryKwh: null,
    endBatteryKwh: null,
    batteryEnergyGainedKwh: null,
    maxPowerKw: null,
    ...fields,
  }
}

describe('trustedBatteryKwh', () => {
  it('prefers a configured capacity on any car', () => {
    expect(trustedBatteryKwh(50, 36.25, 'phev', 20)).toBe(10)
    expect(trustedBatteryKwh(50, 36.25, 'bev', 64)).toBe(32)
    expect(trustedBatteryKwh(null, 36.25, 'bev', 64)).toBeNull()
  })

  it('trusts the reported figure on a BEV only', () => {
    expect(trustedBatteryKwh(50, 36.25, 'bev', null)).toBe(36.25)
    expect(trustedBatteryKwh(50, 36.25, 'phev', null)).toBeNull()
    expect(trustedBatteryKwh(50, 36.25, 'hev', null)).toBeNull()
    expect(trustedBatteryKwh(50, 36.25, 'unknown', null)).toBeNull()
  })
})

describe('energyGainedKwh', () => {
  const charged = session({
    startSocPercent: 20,
    endSocPercent: 80,
    startBatteryKwh: 14.5,
    endBatteryKwh: 58,
  })

  it('takes the reported figures on a BEV', () => {
    expect(energyGainedKwh(charged, { vehicleType: 'bev', configuredCapacityKwh: null })).toBe(43.5)
  })

  it('works from the state of charge with a configured capacity', () => {
    expect(energyGainedKwh(charged, { vehicleType: 'phev', configuredCapacityKwh: 20 })).toBe(12)
  })

  it('gives nothing for a reading that went down', () => {
    const down = session({ startBatteryKwh: 40, endBatteryKwh: 39 })
    expect(energyGainedKwh(down, { vehicleType: 'bev', configuredCapacityKwh: null })).toBeNull()
  })
})

describe('sincePreviousCharge', () => {
  const previous = session({ odometerKm: 1000, endSocPercent: 90, endBatteryKwh: 63 })
  const next = session({ odometerKm: 1150, startSocPercent: 50, startBatteryKwh: 35 })

  it('gives the distance, the energy and the rate on a BEV', () => {
    expect(
      sincePreviousCharge(next, previous, { vehicleType: 'bev', configuredCapacityKwh: null }),
    ).toEqual({ distanceKm: 150, energyKwh: 28, whPerKm: (28 * 1000) / 150 })
  })

  it('leaves the rate out on a plug-in hybrid, whose distance includes petrol', () => {
    expect(
      sincePreviousCharge(next, previous, { vehicleType: 'phev', configuredCapacityKwh: 20 }),
    ).toEqual({ distanceKm: 150, energyKwh: 8, whPerKm: null })
  })

  it('gives nothing without a session before', () => {
    expect(
      sincePreviousCharge(next, null, { vehicleType: 'bev', configuredCapacityKwh: null }),
    ).toEqual({ distanceKm: null, energyKwh: null, whPerKm: null })
  })

  it('gives no rate when the car did not move', () => {
    const parked = session({ odometerKm: 1000, startBatteryKwh: 60 })
    expect(
      sincePreviousCharge(parked, previous, { vehicleType: 'bev', configuredCapacityKwh: null })
        .whPerKm,
    ).toBeNull()
  })
})
