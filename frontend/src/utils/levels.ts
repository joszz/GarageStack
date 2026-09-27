/**
 * How a tank or battery level reads at a glance. Every place that colours a level goes through
 * here, so the dashboard card and the car diagram cannot disagree about when a battery counts as
 * low. The battery's danger line matches the Worker's low-battery notification (below 20 %).
 */
export type LevelVariant = 'success' | 'warning' | 'danger'

interface LevelThresholds {
  /** Below this percentage the level is critical. */
  danger: number
  /** Below this percentage the level is worth keeping an eye on. */
  warning: number
}

const FUEL: LevelThresholds = { danger: 15, warning: 30 }
const EV_BATTERY: LevelThresholds = { danger: 20, warning: 50 }

function variantFor(pct: number | null, thresholds: LevelThresholds): LevelVariant | undefined {
  if (pct === null) return undefined
  if (pct < thresholds.danger) return 'danger'
  if (pct < thresholds.warning) return 'warning'
  return 'success'
}

/** The fuel tank's level as a variant, or undefined when the car reports none. */
export function fuelLevelVariant(pct: number | null): LevelVariant | undefined {
  return variantFor(pct, FUEL)
}

/** The traction battery's state of charge as a variant, or undefined when the car reports none. */
export function evLevelVariant(pct: number | null): LevelVariant | undefined {
  return variantFor(pct, EV_BATTERY)
}
