import { describe, it, expect } from 'vitest'
import {
  chargeEndPercent,
  chargerPowerKw,
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

  it.each(['phev', 'unknown'] as const)(
    'shows a %s no kWh until the real capacity is configured',
    (vehicleType) => {
      // Issue #571: a plug-in hybrid at 95% reported 68.9 of 72.5 kWh; its pack is about 23 kWh
      const reading = hvBatteryReading(
        hv({ evSocPercent: 95, hvSocKwh: 68.88, hvTotalCapacityKwh: 72.5 }),
        vehicleType,
        null,
      )

      expect(reading.socPercent).toBe(95)
      expect(reading.capacityKwh).toBeNull()
      expect(reading.storedKwh).toBeNull()
    },
  )

  it('rescales a plug-in hybrid to its configured capacity', () => {
    const reading = hvBatteryReading(
      hv({ evSocPercent: 95, hvSocKwh: 68.88, hvTotalCapacityKwh: 72.5 }),
      'phev',
      23,
    )

    expect(reading.capacityKwh).toBe(23)
    expect(reading.storedKwh).toBeCloseTo(21.85, 2)
  })

  it('still derives the percentage from the reported pair on a plug-in hybrid', () => {
    const reading = hvBatteryReading(
      hv({ hvSocKwh: 36.25, hvTotalCapacityKwh: 72.5 }),
      'phev',
      null,
    )

    expect(reading.socPercent).toBeCloseTo(50, 5)
    expect(reading.storedKwh).toBeNull()
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

describe('chargerPowerKw', () => {
  it('picks the single-phase figure when the battery takes about that much', () => {
    // Issue #570: a plug-in hybrid on a single-phase charger, the battery taking 4.0 kW
    expect(chargerPowerKw(4.3427, 2.5071, -4.0)).toBe(4.3427)
  })

  it('picks the three-phase figure on a three-phase charger', () => {
    expect(chargerPowerKw(19.05, 11.0, -10.4)).toBe(11.0)
  })

  it('falls back to the three-phase figure without a battery power to compare with', () => {
    expect(chargerPowerKw(4.3, 2.5, null)).toBe(2.5)
    expect(chargerPowerKw(4.3, 2.5, 0)).toBe(2.5)
  })

  it('shows whichever figure there is', () => {
    expect(chargerPowerKw(3.68, null, -3.4)).toBe(3.68)
    expect(chargerPowerKw(null, 11, -10.4)).toBe(11)
    expect(chargerPowerKw(null, null, -3)).toBeNull()
  })
})

describe('chargeEndPercent', () => {
  it('reads the kWh the last charge ended at against the capacity they share', () => {
    // Issue #573: a full plug-in hybrid, both figures on MG's nominal 72.5 kWh pack
    expect(chargeEndPercent({ lastChargeEndingPower: 72.4, hvTotalCapacityKwh: 72.5 })).toBeCloseTo(
      99.86,
      2,
    )
    expect(chargeEndPercent({ lastChargeEndingPower: 40.8, hvTotalCapacityKwh: 51 })).toBeCloseTo(
      80,
    )
  })

  it('stays within 0 to 100 when the two figures drift apart', () => {
    expect(chargeEndPercent({ lastChargeEndingPower: 73, hvTotalCapacityKwh: 72.5 })).toBe(100)
  })

  it('has no percentage without both figures', () => {
    expect(chargeEndPercent({ lastChargeEndingPower: null, hvTotalCapacityKwh: 72.5 })).toBeNull()
    expect(chargeEndPercent({ lastChargeEndingPower: 72.4, hvTotalCapacityKwh: null })).toBeNull()
    expect(chargeEndPercent({ lastChargeEndingPower: 72.4, hvTotalCapacityKwh: 0 })).toBeNull()
  })
})
