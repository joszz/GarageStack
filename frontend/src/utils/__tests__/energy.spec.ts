import { describe, it, expect } from 'vitest'
import {
  dailyCounterTotal,
  energyUnit,
  hvBatteryReading,
  litres,
  litresPer100Km,
  whPerKm,
} from '@/utils/energy'
import type { TelemetrySnapshot } from '@/services/vehicleApi'

type HvFields = Pick<TelemetrySnapshot, 'evSocPercent' | 'hvSocKwh' | 'hvTotalCapacityKwh'>

function hv(fields: Partial<HvFields>): HvFields {
  return { evSocPercent: null, hvSocKwh: null, hvTotalCapacityKwh: null, ...fields }
}

describe('energyUnit', () => {
  it('reads the counters as fuel only on a plain hybrid', () => {
    expect(energyUnit('hev')).toBe('litres')
    expect(energyUnit('phev')).toBe('kwh')
    expect(energyUnit('bev')).toBe('kwh')
    expect(energyUnit('unknown')).toBe('kwh')
  })
})

describe('whPerKm', () => {
  it('converts kWh over km to Wh/km', () => {
    // Issue #284: 16 kWh over 85 km on an MG IM5
    expect(whPerKm(16, 85)).toBeCloseTo(188.24, 2)
  })

  it('returns null without energy or distance', () => {
    expect(whPerKm(null, 10)).toBeNull()
    expect(whPerKm(5, null)).toBeNull()
  })

  it('returns null when no distance has been driven', () => {
    expect(whPerKm(0.4, 0)).toBeNull()
  })
})

describe('litres', () => {
  it('reads the hybrid fuel counter as litres', () => {
    // An MG HS Hybrid+ reporting 394 has burned 3.94 L, not 394 kWh
    expect(litres(394)).toBeCloseTo(3.94, 2)
    expect(litres(0)).toBe(0)
  })

  it('returns null without a reading', () => {
    expect(litres(null)).toBeNull()
  })
})

describe('litresPer100Km', () => {
  it('turns the fuel counter and distance into L/100 km', () => {
    // 3.94 L over 59 km on an MG HS Hybrid+
    expect(litresPer100Km(394, 59)).toBeCloseTo(6.68, 2)
    // A short cold run burns more per km: 1.715 L over 14 km
    expect(litresPer100Km(171.5, 14)).toBeCloseTo(12.25, 2)
  })

  it('returns null without a reading or distance', () => {
    expect(litresPer100Km(null, 59)).toBeNull()
    expect(litresPer100Km(394, null)).toBeNull()
    expect(litresPer100Km(394, 0)).toBeNull()
  })
})

describe('hvBatteryReading', () => {
  it('rescales a hybrid to its configured capacity instead of the gateway figure', () => {
    // The gateway scales 78.3% by an EV-sized 72.5 kWh; the pack actually holds 1.83 kWh
    const reading = hvBatteryReading(
      hv({ evSocPercent: 78.3, hvSocKwh: 56.8, hvTotalCapacityKwh: 72.5 }),
      'hev',
      1.83,
    )

    expect(reading.socPercent).toBe(78.3)
    expect(reading.capacityKwh).toBe(1.83)
    expect(reading.storedKwh).toBeCloseTo(1.43, 2)
  })

  it('shows a hybrid no kWh at all until the real capacity is configured', () => {
    const reading = hvBatteryReading(
      hv({ evSocPercent: 78.3, hvSocKwh: 56.8, hvTotalCapacityKwh: 72.5 }),
      'hev',
      null,
    )

    expect(reading.socPercent).toBe(78.3)
    expect(reading.capacityKwh).toBeNull()
    expect(reading.storedKwh).toBeNull()
  })

  it('keeps what the gateway reports for a car that really has a traction battery', () => {
    const reading = hvBatteryReading(
      hv({ evSocPercent: 80, hvSocKwh: 58, hvTotalCapacityKwh: 72.5 }),
      'bev',
      null,
    )

    expect(reading.capacityKwh).toBe(72.5)
    expect(reading.storedKwh).toBeCloseTo(58, 2)
  })

  it('falls back to the reported ratio when no percentage is published', () => {
    const reading = hvBatteryReading(hv({ hvSocKwh: 36.25, hvTotalCapacityKwh: 72.5 }), 'bev', null)

    expect(reading.socPercent).toBeCloseTo(50, 5)
    expect(reading.storedKwh).toBeCloseTo(36.25, 5)
  })

  it('reports nothing when the vehicle publishes no charge at all', () => {
    const reading = hvBatteryReading(hv({}), 'phev', null)

    expect(reading.socPercent).toBeNull()
    expect(reading.storedKwh).toBeNull()
    expect(reading.capacityKwh).toBeNull()
  })
})

describe('dailyCounterTotal', () => {
  it('returns null without readings', () => {
    expect(dailyCounterTotal([], false)).toBeNull()
    expect(dailyCounterTotal([], true)).toBeNull()
  })

  it('uses the cumulative peak for a completed day', () => {
    expect(dailyCounterTotal([2.1, 9.8, 16.3, 16.3], false)).toBe(16.3)
  })

  it('uses the peak after a counter reset on the current day', () => {
    // 21.4 kWh carried over from yesterday, reset after midnight, then driving resumed
    expect(dailyCounterTotal([21.4, 0, 1.2, 4.5], true)).toBe(4.5)
  })

  it('treats a drop below the noise floor as a reset even when the peak was small', () => {
    // 0.03 is above 5% of the 0.5 peak, so only the noise floor flags it as a reset
    expect(dailyCounterTotal([0.5, 0.03, 0.8], true)).toBe(0.8)
  })

  it('returns null when the counter reset and nothing has been used since', () => {
    expect(dailyCounterTotal([18.7, 0], true)).toBeNull()
  })

  it('returns only the net increase when the counter has not reset yet', () => {
    // A Wh-based floor (50) would flag every one of these as a reset and return 14
    expect(dailyCounterTotal([10, 12.5, 14], true)).toBe(4)
  })

  it('returns null for an unchanged carryover with no driving', () => {
    expect(dailyCounterTotal([7.3, 7.3], true)).toBeNull()
  })

  it('works the same on the much larger numbers of a fuel counter', () => {
    // Hundredths of a litre: yesterday's 907.7 carried over, reset, then 394 burned today
    expect(dailyCounterTotal([907.7, 0, 106.6, 394], true)).toBe(394)
    expect(dailyCounterTotal([106.6, 394], false)).toBe(394)
  })
})
