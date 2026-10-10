import type {
  ClimateSchedule,
  ClimateScheduleOutcome,
  IsoWeekday,
} from '@/services/climateScheduleApi'
import { formatDate, formatTime } from '@/utils/format'
import type { UnitFormatter } from '@/utils/units'

type Translate = (key: string, named?: Record<string, unknown>) => string

export const WEEKDAYS: readonly IsoWeekday[] = [1, 2, 3, 4, 5, 6, 7]
export const WORKWEEK: readonly IsoWeekday[] = [1, 2, 3, 4, 5]
export const WEEKEND: readonly IsoWeekday[] = [6, 7]

/** The name of an ISO weekday in the interface language. 1 January 2024 was a Monday. */
export function weekdayName(day: IsoWeekday, style: 'short' | 'long' = 'short'): string {
  return formatDate(new Date(Date.UTC(2024, 0, day)), { weekday: style, timeZone: 'UTC' })
}

function sameDays(days: readonly IsoWeekday[], set: readonly IsoWeekday[]): boolean {
  return days.length === set.length && set.every((d) => days.includes(d))
}

/** "Every day", "Weekdays", "Weekend", "Once" or the days themselves ("Mon, Wed, Fri"). */
export function daysSummary(days: readonly IsoWeekday[], t: Translate): string {
  if (days.length === 0) return t('climateSchedules.days.once')
  if (sameDays(days, WEEKDAYS)) return t('climateSchedules.days.everyDay')
  if (sameDays(days, WORKWEEK)) return t('climateSchedules.days.weekdays')
  if (sameDays(days, WEEKEND)) return t('climateSchedules.days.weekend')
  return [...days]
    .sort((a, b) => a - b)
    .map((d) => weekdayName(d))
    .join(', ')
}

function localDay(date: Date): number {
  return Math.round(
    new Date(date.getFullYear(), date.getMonth(), date.getDate()).getTime() / 86_400_000,
  )
}

/** When a run is or was, from where the browser stands: "Today 07:30", "Tomorrow 07:30", "Monday 07:30" or a date. */
export function runLabel(iso: string, now: Date, t: Translate): string {
  const at = new Date(iso)
  const time = formatTime(at)
  const days = localDay(at) - localDay(now)
  if (days === 0) return t('climateSchedules.when.today', { time })
  if (days === 1) return t('climateSchedules.when.tomorrow', { time })
  if (days === -1) return t('climateSchedules.when.yesterday', { time })
  if (days > 1 && days < 7) return `${formatDate(at, { weekday: 'long' })} ${time}`
  return `${formatDate(at, { day: 'numeric', month: 'short' })} ${time}`
}

/**
 * A run split for a narrow dashboard card: the time, and the day when it is not today (the
 * weekday within the week, else the date).
 */
export function runParts(iso: string, now: Date): { time: string; day: string | null } {
  const at = new Date(iso)
  const days = localDay(at) - localDay(now)
  const day =
    days === 0
      ? null
      : days > 0 && days < 7
        ? formatDate(at, { weekday: 'short' })
        : formatDate(at, { day: 'numeric', month: 'short' })
  // No leading zero: every character counts on the card.
  return { time: formatTime(at, { hour: 'numeric', minute: '2-digit' }), day }
}

/** What a run sets, in brief: "21 °C · Rear defroster · Seat heating", or the mode for fan only and front defrost. */
export function settingsSummary(
  schedule: Pick<
    ClimateSchedule,
    'mode' | 'temperatureC' | 'rearDefroster' | 'seatLeftLevel' | 'seatRightLevel'
  >,
  t: Translate,
  units: UnitFormatter,
): string {
  const parts = [
    schedule.mode === 'on'
      ? units.format('temperature', schedule.temperatureC, { decimals: 0 })!
      : t(`control.mode.${schedule.mode}`),
  ]
  if (schedule.rearDefroster) parts.push(t('control.rearDefroster'))
  if (schedule.seatLeftLevel > 0 || schedule.seatRightLevel > 0)
    parts.push(t('climateSchedules.seatHeating'))
  return parts.join(' · ')
}

const OUTCOME_VARIANTS: Record<ClimateScheduleOutcome, 'success' | 'warning' | 'danger' | 'info'> =
  {
    running: 'info',
    started: 'success',
    failed: 'danger',
    unconfirmed: 'warning',
    skippedDriving: 'info',
    skippedTemperature: 'info',
    missed: 'warning',
  }

/** The badge colour for how a run went. */
export function outcomeVariant(outcome: ClimateScheduleOutcome) {
  return OUTCOME_VARIANTS[outcome]
}

/** A run that did not do what it should: worth a second look in the list. */
export function needsAttention(schedule: Pick<ClimateSchedule, 'lastRunOutcome'>): boolean {
  const outcome = schedule.lastRunOutcome
  return (
    outcome !== null &&
    OUTCOME_VARIANTS[outcome] !== 'success' &&
    OUTCOME_VARIANTS[outcome] !== 'info'
  )
}

/** The browser's IANA time zone, which a new schedule keeps its start time in. */
export function browserTimeZone(): string {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC'
  } catch {
    return 'UTC'
  }
}

const COMMAND_LABEL_KEYS: Record<string, string> = {
  'climate-temperature': 'control.temperature',
  climate: 'control.climate',
  'rear-defroster': 'control.rearDefroster',
  'seat-left': 'vehicle.climateDetail.seatLeft',
  'seat-right': 'vehicle.climateDetail.seatRight',
}

/** The control a run's command belongs to, as the climate popup names it. */
export function commandLabel(command: string, t: Translate): string {
  const key = COMMAND_LABEL_KEYS[command]
  return key ? t(key) : command
}
