import { computed, ref, shallowReactive, watch } from 'vue'
import { mapApi, MAX_MATCH_POINTS_PER_REQUEST } from '@/services/mapApi'
import type { Trip, TripPoint } from '@/services/vehicleApi'
import { useMapSettingsStore } from '@/stores/settingsMap'
import { decodePolyline } from '@/utils/polyline'
import { downsample } from '@/utils/downsample'

/**
 * Shared cache of snapped trips: a view asks for the trip it is drawing and reads the matched
 * line back as it arrives. Telemetry arrives as fixes tens of seconds apart, so a raw trip line
 * cuts every corner; the API snaps those fixes onto the roads OSM says they were driven on.
 *
 * Module-scoped (as in useReverseGeocode): the map view and anything else drawing a trip share
 * one cache, so selecting a trip a second time draws immediately instead of asking again.
 */

/** A trip's snapped line, with the fixes that produced it still lined up against it. */
export interface TripMatch {
  /** The snapped line, ready to hand to Leaflet. */
  coordinates: [number, number][]
  /** The fixes that were sent, thinned when the trip was denser than the API accepts. */
  points: TripPoint[]
  /** One vertex index into `coordinates` per entry in `points`, in the same order. */
  pointIndexes: number[]
  /** Length of the snapped line, which beats the straight-line distance through the fixes. */
  matchedKm: number
}

// The matcher answers "not right now" while it is rate limited or busy; a trip is worth a couple
// of polite retries before the raw line is left on screen.
const RETRY_DELAY_MS = 1500
const MAX_ATTEMPTS = 3

// Values hold thousands of coordinates, so the map tracks what it holds without proxying each
// one: nothing ever mutates a match in place, it is only ever replaced.
const matches = shallowReactive(new Map<string, TripMatch>())

// Traces the matcher has looked at and found no road for. Remembered for the session so the same
// trip is not sent again every time it is selected.
const unmatchable = new Set<string>()

const inFlight = new Set<string>()
const inFlightCount = ref(0)
const available = ref(true)
// The user's own switch, mirrored from the map settings store (only reachable from a component)
// so the fetcher can read it too.
const wanted = ref(true)
const enabled = computed(() => available.value && wanted.value)

/**
 * What makes two trips the same trace. A finished trip never changes; one still being driven
 * gains fixes, and a longer trip is a different trace worth matching again.
 */
function keyOf(trip: Trip): string {
  return `${trip.startedAt}|${trip.endedAt}|${trip.pointCount}`
}

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

async function fetchMatch(trip: Trip): Promise<void> {
  const key = keyOf(trip)
  if (!enabled.value || matches.has(key) || unmatchable.has(key) || inFlight.has(key)) return

  const points = downsample(trip.points, MAX_MATCH_POINTS_PER_REQUEST)
  if (points.length < 2) return

  inFlight.add(key)
  inFlightCount.value += 1
  try {
    for (let attempt = 0; attempt < MAX_ATTEMPTS; attempt++) {
      let response
      try {
        response = await mapApi.matchTrip(
          points.map((p) => ({ lat: p.latitude, lng: p.longitude })),
        )
      } catch {
        // Offline, unauthenticated, or rate limited by our own API: leave the raw line up and
        // stop this run. Selecting the trip again starts a new one.
        return
      }

      if (!response.available) {
        // Switched off for this deployment: stop asking, and drop what is already drawn so the
        // map does not keep showing snapped lines the server will no longer refresh.
        available.value = false
        matches.clear()
        return
      }

      if (response.matched && response.shape && response.pointIndexes) {
        matches.set(key, {
          coordinates: decodePolyline(response.shape),
          points,
          pointIndexes: response.pointIndexes,
          matchedKm: response.matchedKm,
        })
        return
      }

      // The matcher looked and recognised no road under these fixes; that will not change by
      // asking again.
      if (!response.pending) {
        unmatchable.add(key)
        return
      }

      if (attempt < MAX_ATTEMPTS - 1) await delay(RETRY_DELAY_MS)
    }
  } finally {
    inFlight.delete(key)
    inFlightCount.value -= 1
  }
}

export function useTripMatch() {
  const settings = useMapSettingsStore()
  wanted.value = settings.snapToRoadsEnabled

  watch(
    () => settings.snapToRoadsEnabled,
    (on) => {
      wanted.value = on
    },
  )

  /** Asks for a trip's snapped line; one already cached, refused, or in flight is skipped. */
  function requestMatch(trip: Trip | null): void {
    if (trip) void fetchMatch(trip)
  }

  /** A trip's snapped line, or null while it is unknown. Reactive: fills in when the answer lands. */
  function matchFor(trip: Trip | null): TripMatch | null {
    if (!enabled.value || !trip) return null
    return matches.get(keyOf(trip)) ?? null
  }

  return {
    requestMatch,
    matchFor,
    /**
     * False when snapping is switched off, either by the user's setting or by the deployment.
     * Callers then draw the raw fixes.
     */
    snapEnabled: enabled,
    /** True while a trip is being matched, so a caller can say so rather than look stuck. */
    matching: computed(() => inFlightCount.value > 0),
  }
}
