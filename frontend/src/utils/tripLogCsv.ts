import type { TripLogEntry } from '@/services/tripLogApi'
import { csvFormatFor, toCsv, type CsvCell } from '@/utils/csv'
import { endLabel, loggedDistanceKm, periodKey, type TripLogPeriod } from '@/utils/tripLog'

// `t` is injected rather than obtained via useI18n() so this stays a plain, directly testable
// function, as in tripRows.
type Translate = (key: string) => string

export interface TripLogCsvContext {
  locale: string
  t: Translate
  /** False when place names are switched off: the ends are then written as coordinates. */
  placesShown: boolean
}

function oneDecimal(value: number | null): number | null {
  return value === null ? null : Math.round(value * 10) / 10
}

/**
 * The trip log as CSV, one trip per line, with the columns a Dutch "rittenregistratie" asks for:
 * date, where from and where to, the odometer at either end, the distance, and whether the trip
 * was business or private. Times and notes ride along; a trip nobody classified yet has a blank
 * purpose rather than a guess.
 */
export function tripLogCsv(entries: readonly TripLogEntry[], ctx: TripLogCsvContext): string {
  const { locale, t, placesShown } = ctx
  const time = (iso: string) =>
    new Date(iso).toLocaleTimeString(locale, { hour: '2-digit', minute: '2-digit' })

  const header: CsvCell[] = [
    t('tripLog.csv.date'),
    t('tripLog.csv.departed'),
    t('tripLog.csv.arrived'),
    t('tripLog.csv.from'),
    t('tripLog.csv.to'),
    t('tripLog.csv.odometerStart'),
    t('tripLog.csv.odometerEnd'),
    t('tripLog.csv.distance'),
    t('tripLog.csv.purpose'),
    t('tripLog.csv.notes'),
  ]

  const rows = entries.map((entry): CsvCell[] => [
    new Date(entry.startedAt).toLocaleDateString(locale),
    time(entry.startedAt),
    time(entry.endedAt),
    endLabel(placesShown ? entry.startPlace : null, entry.startLatitude, entry.startLongitude),
    endLabel(placesShown ? entry.endPlace : null, entry.endLatitude, entry.endLongitude),
    oneDecimal(entry.odometerStartKm),
    oneDecimal(entry.odometerEndKm),
    oneDecimal(loggedDistanceKm(entry)),
    entry.purpose ? t(`tripLog.purpose.${entry.purpose}`) : null,
    entry.notes,
  ])

  return toCsv([header, ...rows], csvFormatFor(locale))
}

/** "trip-log-2026-09.csv": sorts by period in a folder, whatever the interface language. */
export function tripLogFileName(period: TripLogPeriod): string {
  return `trip-log-${periodKey(period)}.csv`
}
