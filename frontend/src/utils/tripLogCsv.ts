import type { TripLogEntry } from '@/services/tripLogApi'
import { csvFormatFor, toCsv, type CsvCell } from '@/utils/csv'
import { endLabel, loggedDistanceKm, periodKey, type TripLogPeriod } from '@/utils/tripLog'
import type { UnitFormatter } from '@/utils/units'

// `t` is injected rather than obtained via useI18n() so this stays a plain, directly testable
// function, as in tripRows.
type Translate = (key: string, named?: Record<string, unknown>) => string

export interface TripLogCsvContext {
  locale: string
  t: Translate
  /** The distances are written in the unit the log is read in, and the headers say which. */
  units: UnitFormatter
  /** False when place names are switched off: the ends are then written as coordinates. */
  placesShown: boolean
}

/**
 * The trip log as CSV, one trip per line, with the columns a Dutch "rittenregistratie" asks for:
 * date, where from and where to, the odometer at either end, the distance, and whether the trip
 * was business or private. Times and notes ride along; a trip nobody classified yet has a blank
 * purpose rather than a guess.
 */
export function tripLogCsv(entries: readonly TripLogEntry[], ctx: TripLogCsvContext): string {
  const { locale, t, units, placesShown } = ctx
  const time = (iso: string) =>
    new Date(iso).toLocaleTimeString(locale, { hour: '2-digit', minute: '2-digit' })
  // A number rather than text, so a spreadsheet can add the column up.
  const distance = (km: number | null) =>
    km === null ? null : Math.round(units.convert('distance', km) * 10) / 10
  const unit = { unit: units.symbol('distance') }

  const header: CsvCell[] = [
    t('tripLog.csv.date'),
    t('tripLog.csv.departed'),
    t('tripLog.csv.arrived'),
    t('tripLog.csv.from'),
    t('tripLog.csv.to'),
    t('tripLog.csv.odometerStart', unit),
    t('tripLog.csv.odometerEnd', unit),
    t('tripLog.csv.distance', unit),
    t('tripLog.csv.purpose'),
    t('tripLog.csv.notes'),
  ]

  const rows = entries.map((entry): CsvCell[] => [
    new Date(entry.startedAt).toLocaleDateString(locale),
    time(entry.startedAt),
    time(entry.endedAt),
    endLabel(placesShown ? entry.startPlace : null, entry.startLatitude, entry.startLongitude),
    endLabel(placesShown ? entry.endPlace : null, entry.endLatitude, entry.endLongitude),
    distance(entry.odometerStartKm),
    distance(entry.odometerEndKm),
    distance(loggedDistanceKm(entry)),
    entry.purpose ? t(`tripLog.purpose.${entry.purpose}`) : null,
    entry.notes,
  ])

  return toCsv([header, ...rows], csvFormatFor(locale))
}

/** "trip-log-2026-09.csv": sorts by period in a folder, whatever the interface language. */
export function tripLogFileName(period: TripLogPeriod): string {
  return `trip-log-${periodKey(period)}.csv`
}
