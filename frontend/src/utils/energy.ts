import type { DailyStat, TelemetrySnapshot, VehicleType } from '@/services/vehicleApi'
import type { Quantity } from '@/utils/units'

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

/**
 * What the energy counters measure on this drivetrain. Only a plain hybrid reports fuel: a BEV
 * and a PHEV both cycle a traction battery the counter can describe in kWh.
 */
export function energyUnit(vehicleType: VehicleType): EnergyUnit {
  return vehicleType === 'hev' ? 'litres' : 'kwh'
}

// Not every plug-in car keeps its day counter straight. An MG ZS EV (issue #551) adds 33 to 35 kWh
// to it at some polls while parked, so a 4 km day read 134 kWh. Its since-last-charge counter
// comes out of the same charge status message and matches the state of charge drop to the
// hundredth, so that counter is what the day counter is checked against: the API does it per day
// over every reading (DailyStatsBuilder), and todaysEnergyKwh below does it for the live reading.

// Today's use is part of what left the battery since its last charge. The factor leaves room for
// regeneration, which the since-last-charge counter takes back off and a day counter may not, and
// the margin for rounding. The API's per-day check uses the same two numbers.
const SINCE_CHARGE_FACTOR = 1.5
const SINCE_CHARGE_MARGIN_KWH = 1

// A charge this recent may have happened today, after which today's use can exceed what the
// since-last-charge counter has counted.
const RECENT_CHARGE_MS = 24 * 60 * 60 * 1000

/**
 * A day's total from the statistics, in the counter's own unit. On a car that plugs in, the day
 * counter gives way to the since-last-charge counter on a day the API found it rising past it.
 */
export function dailyCounterTotal(
  day: Pick<DailyStat, 'counterTotal' | 'sinceChargeTotal' | 'counterUnbacked'>,
  vehicleType: VehicleType,
): number | null {
  return energyUnit(vehicleType) === 'kwh' && day.counterUnbacked
    ? day.sinceChargeTotal
    : day.counterTotal
}

/**
 * Today's energy off the day counter in kWh, or null when it holds more than had left the battery
 * since its last charge. Without a since-last-charge reading, or after a charge that may have been
 * today, there is nothing to check it against and the counter stands.
 */
export function todaysEnergyKwh(
  status: Pick<
    TelemetrySnapshot,
    'powerUsageOfDay' | 'powerUsageSinceLastCharge' | 'isCharging' | 'chargingLastEndAt'
  >,
  now: number = Date.now(),
): number | null {
  const today = status.powerUsageOfDay
  const sinceCharge = status.powerUsageSinceLastCharge
  if (today === null || sinceCharge === null || status.isCharging) return today
  const lastEnd = status.chargingLastEndAt ? Date.parse(status.chargingLastEndAt) : NaN
  if (now - lastEnd < RECENT_CHARGE_MS) return today
  return today > sinceCharge * SINCE_CHARGE_FACTOR + SINCE_CHARGE_MARGIN_KWH ? null : today
}

export function whPerKm(energyKwh: number | null, distanceKm: number | null): number | null {
  if (energyKwh === null || distanceKm === null || distanceKm <= 0) return null
  return (energyKwh * 1000) / distanceKm
}

/**
 * Below this distance an efficiency is mostly rounding: some cars count their day's distance in
 * whole kilometres (the ZS EV in issue #551 does), so a 4 km day could be anything from 4 to 5.
 */
export const MIN_EFFICIENCY_DISTANCE_KM = 5

/** What the energy counter counted, in its own unit, over the days that also counted a distance. */
export interface CounterOverDistance {
  counter: number
  distanceKm: number
}

/**
 * The energy (or fuel) counter's total and the distance counter's total over the days that report
 * both, so the two always describe the same driving. A day the car stood still still counts, at
 * 0 km: what the battery lost while parked is part of what the car used. Null without such a day.
 */
export function counterOverDistance(
  days: readonly Pick<
    DailyStat,
    'counterTotal' | 'sinceChargeTotal' | 'counterUnbacked' | 'distanceKm'
  >[],
  vehicleType: VehicleType,
): CounterOverDistance | null {
  let paired: CounterOverDistance | null = null
  for (const day of days) {
    const counter = dailyCounterTotal(day, vehicleType)
    if (counter === null || day.distanceKm === null) continue
    paired = addCounterOverDistance(paired, { counter, distanceKm: day.distanceKm })
  }
  return paired
}

/** An efficiency in the API's metric unit, with the quantity that unit belongs to. */
export interface Efficiency {
  quantity: Extract<Quantity, 'energyPerDistance' | 'fuelConsumption'>
  /** Wh/km on a plug-in car, L/100 km on a hybrid. */
  metric: number
}

/**
 * How much the car used per distance: Wh/km off a plug-in car's energy counter, L/100 km off a
 * hybrid's fuel counter. Null without data, or over too short a distance to mean anything.
 */
export function efficiency(used: CounterOverDistance | null, unit: EnergyUnit): Efficiency | null {
  if (!used || used.distanceKm < MIN_EFFICIENCY_DISTANCE_KM) return null
  const metric =
    unit === 'litres'
      ? litresPer100Km(used.counter, used.distanceKm)
      : whPerKm(used.counter, used.distanceKm)
  if (metric === null) return null
  return { quantity: unit === 'litres' ? 'fuelConsumption' : 'energyPerDistance', metric }
}

/** Two stretches of driving as one, or whichever of them there is. */
export function addCounterOverDistance(
  a: CounterOverDistance | null,
  b: CounterOverDistance | null,
): CounterOverDistance | null {
  if (!a || !b) return a ?? b
  return { counter: a.counter + b.counter, distanceKm: a.distanceKm + b.distanceKm }
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
