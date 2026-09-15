// Energy counters from the gateway (powerUsageOfDay, powerUsageSinceLastCharge) are published
// in kWh, not Wh. Every calculation on them goes through this file so the unit lives in one place.

// A reading below this is treated as a counter reset rather than noise
const RESET_NOISE_FLOOR_KWH = 0.05
// A reading below this fraction of the running peak is treated as a counter reset
const RESET_PEAK_FRACTION = 0.05

export function whPerKm(energyKwh: number | null, distanceKm: number | null): number | null {
  if (energyKwh === null || distanceKm === null || distanceKm <= 0) return null
  return (energyKwh * 1000) / distanceKm
}

// Energy consumed on one day from the cumulative powerUsageOfDay readings, oldest first.
export function dailyEnergyKwh(readings: readonly number[], isPartialDay: boolean): number | null {
  if (!readings.length) return null

  // Completed days: cumulative peak = day's total
  if (!isPartialDay) return Math.max(...readings)

  // Today (partial day): detect a true counter reset so we don't show yesterday's carryover
  // when no driving has happened yet after midnight
  let lastResetIdx = -1
  let peak = readings[0]!
  for (let i = 1; i < readings.length; i++) {
    peak = Math.max(peak, readings[i - 1]!)
    if (readings[i]! < Math.max(peak * RESET_PEAK_FRACTION, RESET_NOISE_FLOOR_KWH)) {
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
