export const MS_PER_DAY = 86_400_000

/** A number as two digits, for dates and clock times: 7 is "07". */
export function pad2(n: number): string {
  return String(n).padStart(2, '0')
}

/** Local midnight at the start of the day `date` falls on. */
export function startOfLocalDay(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate())
}

/**
 * The local calendar day as "YYYY-MM-DD", which is also what a date input takes. Not
 * toISOString(): that is the day in UTC, which in Europe is still yesterday just after midnight.
 */
export function localDateKey(date: Date): string {
  const y = date.getFullYear()
  return `${y}-${pad2(date.getMonth() + 1)}-${pad2(date.getDate())}`
}

/**
 * ISO timestamp for exactly `days` days before now. The "from" bound the dashboard and map use
 * for their trip queries.
 */
export function daysAgoIso(days: number, now = Date.now()): string {
  return new Date(now - days * MS_PER_DAY).toISOString()
}

/**
 * The browser's IANA time zone: what a new climate schedule keeps its start time in, and what the
 * statistics page's days run midnight to midnight in.
 */
export function browserTimeZone(): string {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC'
  } catch {
    return 'UTC'
  }
}

const TIME_OF_DAY = /^([01]\d|2[0-3]):[0-5]\d/

/**
 * A time of day as "HH:mm", from one with or without seconds, so times the car reports compare
 * with the ones a form holds; null for anything that is not one.
 */
export function timeOfDay(value: string | null | undefined): string | null {
  return value && TIME_OF_DAY.test(value) ? value.slice(0, 5) : null
}
