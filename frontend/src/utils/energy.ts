import type { TelemetrySnapshot, VehicleType } from '@/services/vehicleApi'

// The gateway publishes powerUsageOfDay and powerUsageSinceLastCharge on the same topics for
// every drivetrain, but they do not measure the same thing on every drivetrain. A car with a
// traction battery reports electrical energy in kWh. A plain hybrid has no such counter, so the
// trip computer puts its fuel total there instead, in hundredths of a litre: read as kWh that
// turns a 59 km day into "394 kWh". Every calculation on those counters goes through this file,
// so which unit applies is decided in one place.
//
// The hundredths-of-a-litre scale is measured, not guessed. Regressing the counter against the
// fuel gauge within each tank over 90 days of an MG HS Hybrid+ (20 tanks, r2 0.94 to 0.997)
// implies a 54.7 L tank against the 55 L the car actually has.

export type EnergyUnit = 'kwh' | 'litres'

/** Hundredths of a litre per litre, the scale a hybrid reports its fuel total in. */
const COUNTER_UNITS_PER_LITRE = 100

// A reading below this is treated as a counter reset rather than noise
const RESET_NOISE_FLOOR = 0.05
// A reading below this fraction of the running peak is treated as a counter reset
const RESET_PEAK_FRACTION = 0.05

/**
 * What the energy counters measure on this drivetrain. Only a plain hybrid reports fuel: a BEV
 * and a PHEV both cycle a traction battery the counter can describe in kWh.
 */
export function energyUnit(vehicleType: VehicleType): EnergyUnit {
  return vehicleType === 'hev' ? 'litres' : 'kwh'
}

export function whPerKm(energyKwh: number | null, distanceKm: number | null): number | null {
  if (energyKwh === null || distanceKm === null || distanceKm <= 0) return null
  return (energyKwh * 1000) / distanceKm
}

/** Litres burned, from a hybrid's fuel counter. */
export function litres(counter: number | null): number | null {
  return counter === null ? null : counter / COUNTER_UNITS_PER_LITRE
}

/** Fuel consumption in L/100 km, from a hybrid's fuel counter over the distance it covers. */
export function litresPer100Km(counter: number | null, distanceKm: number | null): number | null {
  const used = litres(counter)
  if (used === null || distanceKm === null || distanceKm <= 0) return null
  return (used / distanceKm) * 100
}

/**
 * The high-voltage battery as it should be shown, given what the deployment knows about the car.
 *
 * The gateway does not read capacity off the pack: it scales the BMS percentage by an EV-sized
 * default, which is right for a car that has one and off by a factor of forty on a hybrid whose
 * buffer is under 2 kWh (an MG HS Hybrid+ carries 1.83 kWh and is reported as 72.5). So a
 * configured capacity always wins, and a hybrid without one shows percentages only rather than
 * a confident wrong number of kWh.
 */
export interface HvBatteryReading {
  socPercent: number | null
  storedKwh: number | null
  capacityKwh: number | null
}

export function hvBatteryReading(
  status: Pick<TelemetrySnapshot, 'evSocPercent' | 'hvSocKwh' | 'hvTotalCapacityKwh'>,
  vehicleType: VehicleType,
  configuredCapacityKwh: number | null,
): HvBatteryReading {
  const reported = status.hvTotalCapacityKwh
  const capacityKwh = configuredCapacityKwh ?? (vehicleType === 'hev' ? null : reported)

  // drivetrain/soc is the BMS reading itself. The ratio behind it is the fallback for a car that
  // publishes only the derived pair, and survives rescaling because both sides carry the same
  // assumed capacity.
  const socPercent =
    status.evSocPercent ??
    (status.hvSocKwh !== null && reported !== null && reported > 0
      ? (status.hvSocKwh / reported) * 100
      : null)

  return {
    socPercent,
    storedKwh:
      capacityKwh !== null && socPercent !== null ? (capacityKwh * socPercent) / 100 : null,
    capacityKwh,
  }
}

/**
 * One day's total from the cumulative counter readings, oldest first. Unit-agnostic: it works on
 * the running peak and on resets, which behave the same whether the counter holds kWh or litres.
 */
export function dailyCounterTotal(
  readings: readonly number[],
  isPartialDay: boolean,
): number | null {
  if (!readings.length) return null

  // Completed days: cumulative peak = day's total
  if (!isPartialDay) return Math.max(...readings)

  // Today (partial day): detect a true counter reset so we don't show yesterday's carryover
  // when no driving has happened yet after midnight
  let lastResetIdx = -1
  let peak = readings[0]!
  for (let i = 1; i < readings.length; i++) {
    peak = Math.max(peak, readings[i - 1]!)
    if (readings[i]! < Math.max(peak * RESET_PEAK_FRACTION, RESET_NOISE_FLOOR)) {
      lastResetIdx = i
    }
  }

  if (lastResetIdx >= 0) {
    const usage = Math.max(...readings.slice(lastResetIdx))
    return usage > 0 ? usage : null
  }

  // No reset yet: net increase only (carryover with no driving -> delta 0 -> null)
  const delta = Math.max(...readings) - Math.min(...readings)
  return delta > 0 ? delta : null
}
