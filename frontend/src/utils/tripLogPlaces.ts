import { GEOCODE_MAX_ATTEMPTS, GEOCODE_RETRY_DELAY_MS } from '@/services/mapApi'
import {
  tripLogApi,
  MAX_TRIPS_PER_PLACES_REQUEST,
  type TripLogEntry,
  type TripPlaces,
} from '@/services/tripLogApi'
import { missingPlaces } from '@/utils/tripLog'
import { resolveInRounds } from '@/utils/resolveInRounds'

/** The trips with the addresses the server found filled in; an end it found nothing for stays as it was. */
export function withPlaces(
  entries: readonly TripLogEntry[],
  found: readonly TripPlaces[],
): TripLogEntry[] {
  const byId = new Map(found.map((places) => [places.id, places]))
  return entries.map((entry) => {
    const places = byId.get(entry.id)
    return places
      ? {
          ...entry,
          startPlace: places.startPlace ?? entry.startPlace,
          endPlace: places.endPlace ?? entry.endPlace,
        }
      : entry
  })
}

/** What a lookup works on: the trip log page's list, or the trips an export is about to write. */
export interface TripPlaceLookup {
  /** The trips as they are now; asked again every round, so the list may change in between. */
  entries(): readonly TripLogEntry[]
  /** Keeps what a round found. */
  apply(found: readonly TripPlaces[]): void
  /** Whether to go on; asked before every round. */
  active(): boolean
  /** Runs as the lookup ends, however it ends. */
  onEnd(): void
}

/**
 * Looks up the addresses the trips are still missing, a batch per round with a pause between
 * rounds, until every one is known or given up on. The server keeps what it finds, so a trip is
 * looked up only once. Resolves to false when the deployment has geocoding switched off; a
 * request that fails outright rejects.
 */
export async function lookUpTripPlaces(
  vin: string,
  language: string,
  lookup: TripPlaceLookup,
): Promise<boolean> {
  let available = true
  const attempts = new Map<number, number>()
  const wanted = (entry: TripLogEntry) =>
    missingPlaces(entry) && (attempts.get(entry.id) ?? 0) < GEOCODE_MAX_ATTEMPTS

  await resolveInRounds<TripLogEntry>(
    {
      active: lookup.active,
      nextBatch: () => lookup.entries().filter(wanted).slice(0, MAX_TRIPS_PER_PLACES_REQUEST),
      ask: async (batch) => {
        const result = await tripLogApi.resolvePlaces(
          vin,
          batch.map((entry) => entry.id),
          language,
        )
        if (!result.available) {
          available = false
          return null
        }
        lookup.apply(result.trips)
        const current = new Map(lookup.entries().map((entry) => [entry.id, entry]))
        return batch.filter((entry) => {
          const now = current.get(entry.id)
          return now !== undefined && missingPlaces(now)
        })
      },
      countAttempt: (entry) => attempts.set(entry.id, (attempts.get(entry.id) ?? 0) + 1),
      hasMore: () => lookup.entries().some(wanted),
      onEnd: lookup.onEnd,
    },
    GEOCODE_RETRY_DELAY_MS,
  )
  return available
}
