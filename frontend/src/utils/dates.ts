const MS_PER_DAY = 86_400_000

/**
 * ISO timestamp for exactly `days` days before now. The "from" bound the dashboard and map use
 * for their trip queries.
 */
export function daysAgoIso(days: number, now = Date.now()): string {
  return new Date(now - days * MS_PER_DAY).toISOString()
}

/**
 * ISO timestamp for local midnight `days` days ago. The statistics page groups history by
 * local calendar day, so its range has to start on a day boundary or the first bucket would
 * be a partial day.
 */
export function startOfLocalDayDaysAgoIso(days: number, now = new Date()): string {
  const start = new Date(now)
  start.setDate(start.getDate() - days)
  return new Date(start.getFullYear(), start.getMonth(), start.getDate()).toISOString()
}
