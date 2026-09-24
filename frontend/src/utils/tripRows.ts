import type { Trip } from '@/services/vehicleApi'

// `t` is injected rather than obtained via useI18n() so these stay plain, directly testable
// functions, as in useVehicleAlerts. The structural type avoids coupling to a locale's
// message-key generics.
type Translate = (key: string, named?: Record<string, unknown>) => string

/** Whether a trip row leads with its route, a placeholder, or falls back to its date. */
export type TripRowMode = 'route' | 'pending' | 'date'

export interface TripRow {
  mode: TripRowMode
  from: string | null
  /** Null for a round trip, so one city is named once instead of on both sides of an arrow. */
  to: string | null
  routeTitle: string
  dateLabel: string
  timeLabel: string
  meta: string
  metaTitle: string
}

export interface TripRowContext {
  /** City at the trip's first point, or null while unknown. */
  fromCity: string | null
  /** City at the trip's last point, or null while unknown. Ignored for a trip in progress. */
  toCity: string | null
  /**
   * A trip still being driven has no destination yet, so it is named after where it started.
   * Its last point is wherever the car is right now, and looking that up again every time the
   * car moves would be a stream of requests for a name that keeps changing anyway.
   */
  inProgress?: boolean
  /** False when this trip can never gain a route: no coordinates, or geocoding is switched off. */
  canResolve: boolean
  /** True while lookups are in flight, which is what a placeholder means rather than a fallback. */
  resolving: boolean
  locale: string
  t: Translate
}

export function formatTripDuration(startedAt: string, endedAt: string, t: Translate): string {
  const ms = new Date(endedAt).getTime() - new Date(startedAt).getTime()
  const mins = Math.round(ms / 60_000)
  if (mins < 60) return t('trips.durationMinutes', { n: mins })
  return t('trips.durationHoursMinutes', { h: Math.floor(mins / 60), m: mins % 60 })
}

/**
 * Everything one row of the trip list shows. Kept out of the view so the fallbacks (no place
 * names yet, no place names at all) are decided in one testable place rather than in template
 * conditionals.
 */
export function buildTripRow(trip: Trip, ctx: TripRowContext): TripRow {
  const { fromCity, toCity, canResolve, resolving, locale, t, inProgress = false } = ctx
  const destination = inProgress ? null : toCity

  const startedAt = new Date(trip.startedAt)
  const dateLabel = startedAt.toLocaleDateString(locale)
  const dateShort = startedAt.toLocaleDateString(locale, { day: 'numeric', month: 'short' })
  const timeLabel = startedAt.toLocaleTimeString(locale, { hour: '2-digit', minute: '2-digit' })

  const distance = `${trip.distanceKm} ${t('common.km')}`
  const duration = formatTripDuration(trip.startedAt, trip.endedAt, t)
  const points = `${trip.pointCount} ${t('trips.points')}`

  // Both ends or neither: half a route ("Zwolle to ?") reads worse than waiting a moment. A trip
  // in progress is the exception, since it has only an origin to show.
  const hasRoute = fromCity !== null && (inProgress || destination !== null)
  const mode: TripRowMode = hasRoute ? 'route' : canResolve && resolving ? 'pending' : 'date'

  return {
    mode,
    from: fromCity,
    to: destination === fromCity ? null : destination,
    routeTitle: [fromCity, destination].filter(Boolean).join(' → '),
    dateLabel,
    timeLabel,
    // Leading with the route moves the date into the meta line; without one the date already
    // leads, so the meta keeps the detail it has always shown there instead of repeating it.
    meta:
      mode === 'date'
        ? [distance, duration, points].join(' · ')
        : [dateShort, distance, duration].join(' · '),
    metaTitle: [dateLabel, timeLabel, distance, duration, points].join(' · '),
  }
}
