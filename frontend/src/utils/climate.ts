import type { ClimateMode, TelemetrySnapshot } from '@/services/vehicleApi'
import type { ChipOption } from './chips'

/** The modes climate can run in; off is the climate switch rather than a mode. */
export type ClimateOnMode = Exclude<ClimateMode, 'off'>

export const CLIMATE_ON_MODES: readonly ClimateOnMode[] = ['on', 'blowingonly', 'front']

const MODE_ICONS: Record<ClimateOnMode, string> = {
  on: 'wind',
  blowingonly: 'fan',
  front: 'snowflake',
}

/**
 * What climate is doing, from both fields that describe it: switched off wins, and climate that is
 * on without a mode (the older climate/on topic names none) counts as normal climate. Null when
 * neither field is known.
 */
export function climateModeOf(
  status: Pick<TelemetrySnapshot, 'climateOn' | 'climateMode'>,
): ClimateMode | null {
  if (status.climateOn === false) return 'off'
  if (status.climateOn === true)
    return status.climateMode && status.climateMode !== 'off' ? status.climateMode : 'on'
  return status.climateMode ?? null
}

/** The climate modes as chips, labelled from control.mode.*. */
export function climateModeOptions(t: (key: string) => string): ChipOption<ClimateOnMode>[] {
  return CLIMATE_ON_MODES.map((mode) => ({
    value: mode,
    label: t(`control.mode.${mode}`),
    icon: MODE_ICONS[mode],
  }))
}

/** The car takes whole degrees Celsius in this range for its target temperature. */
export const CLIMATE_TEMPERATURE_MIN_C = 16
export const CLIMATE_TEMPERATURE_MAX_C = 28
/** Seat heating runs from 0 (off) to 3 (high). */
export const SEAT_LEVEL_MAX = 3
