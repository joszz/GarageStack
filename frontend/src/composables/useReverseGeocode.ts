import { computed, reactive, ref, watch } from 'vue'
import {
  mapApi,
  GEOCODE_MAX_ATTEMPTS,
  GEOCODE_RETRY_DELAY_MS,
  MAX_GEOCODE_POINTS_PER_REQUEST,
  type GeocodePrecision,
  type GeoPoint,
  type Place,
} from '@/services/mapApi'
import { useUiSettingsStore } from '@/stores/settingsUi'

/**
 * Shared reverse-geocoding cache: components ask for the places they want to label and read them
 * back as they arrive. Coordinates are queued rather than fetched one by one, because the
 * geocoder upstream of the API answers about one lookup per second - a cold trip list fills in
 * over a few seconds instead of blocking, and a warm one is answered by the server's cache in a
 * single request.
 *
 * Module-scoped on purpose (as in useTyrePressureThresholds): the trip list and the dashboard's
 * location card share one cache and one queue instead of each running their own.
 */

// Cap on the points remembered for a language switch; browsing a long history should not grow
// this without bound.
const MAX_TRACKED_POINTS = 200

// Coordinate precision of a cache key: ~1m, fine enough that two distinct stops never collide
// and coarse enough that the same stop is one key. The server does its own, coarser grouping.
const KEY_DECIMALS = 5

interface QueuedPoint {
  lat: number
  lng: number
  precision: GeocodePrecision
  attempts: number
}

interface TrackedPoint {
  lat: number
  lng: number
  precision: GeocodePrecision
}

const places = reactive(new Map<string, Place>())
const queue = new Map<string, QueuedPoint>()
const tracked = new Map<string, TrackedPoint>()
const available = ref(true)
// The user's own switch, mirrored from the UI settings store (which is only reachable from a
// component) so the queue and the pump can read it too.
const wanted = ref(true)
const enabled = computed(() => available.value && wanted.value)
const resolving = ref(false)
const language = ref<string>('en')
let pumpRunning = false
let lastPrecision: GeocodePrecision | null = null

function keyOf(lat: number, lng: number, precision: GeocodePrecision, lang: string): string {
  return `${precision}|${lang}|${lat.toFixed(KEY_DECIMALS)},${lng.toFixed(KEY_DECIMALS)}`
}

function trackKey(lat: number, lng: number, precision: GeocodePrecision): string {
  return `${precision}|${lat.toFixed(KEY_DECIMALS)},${lng.toFixed(KEY_DECIMALS)}`
}

function track(point: TrackedPoint) {
  const key = trackKey(point.lat, point.lng, point.precision)
  // Re-insert so the Map's insertion order stays "least recently asked about first".
  tracked.delete(key)
  tracked.set(key, point)
  while (tracked.size > MAX_TRACKED_POINTS) {
    const oldest = tracked.keys().next()
    if (oldest.done) break
    tracked.delete(oldest.value)
  }
}

function enqueue(point: TrackedPoint, lang: string) {
  const key = keyOf(point.lat, point.lng, point.precision, lang)
  if (places.has(key) || queue.has(key)) return
  queue.set(key, { ...point, attempts: 0 })
}

/**
 * The next batch to send: one precision per request, since that is what the endpoint takes.
 * Precisions take turns, so the one address behind a long list of trip cities is not starved
 * for the dozen rounds that list needs.
 */
function takeBatch(): { key: string; point: QueuedPoint }[] {
  const queued = [...queue]
  if (queued.length === 0) return []

  const next = queued.find(([, point]) => point.precision !== lastPrecision) ?? queued[0]!
  const precision = next[1].precision
  lastPrecision = precision

  const batch: { key: string; point: QueuedPoint }[] = []
  for (const [key, point] of queued) {
    if (point.precision !== precision) continue
    batch.push({ key, point })
    if (batch.length >= MAX_GEOCODE_POINTS_PER_REQUEST) break
  }
  return batch
}

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

async function pump() {
  if (pumpRunning) return
  pumpRunning = true
  resolving.value = true
  try {
    while (queue.size > 0 && enabled.value) {
      const lang = language.value
      const batch = takeBatch()
      if (batch.length === 0) break

      let response
      try {
        response = await mapApi.reverseGeocode(
          batch.map(({ point }) => ({ lat: point.lat, lng: point.lng }) as GeoPoint),
          batch[0]!.point.precision,
          lang,
        )
      } catch {
        // Offline, unauthenticated, or rate-limited: count the attempt and stop this run rather
        // than hammering a failing API. A later requestPlaces call starts the pump again.
        for (const { key, point } of batch) {
          point.attempts += 1
          if (point.attempts >= GEOCODE_MAX_ATTEMPTS) queue.delete(key)
        }
        break
      }

      if (!response.available) {
        available.value = false
        queue.clear()
        break
      }

      const unresolved: { key: string; point: QueuedPoint }[] = []
      for (const [index, entry] of batch.entries()) {
        const place = response.results[index]
        if (place) {
          places.set(entry.key, place)
          queue.delete(entry.key)
        } else {
          unresolved.push(entry)
        }
      }

      // A server answering some of the batch is working through its own upstream budget, not
      // failing: only a round that resolved nothing counts as an attempt, otherwise a long list
      // would abandon most of its coordinates after a few rounds of normal progress.
      if (unresolved.length === batch.length) {
        for (const { key, point } of unresolved) {
          point.attempts += 1
          if (point.attempts >= GEOCODE_MAX_ATTEMPTS) queue.delete(key)
        }
      }

      if (queue.size > 0) await delay(GEOCODE_RETRY_DELAY_MS)
    }
  } finally {
    pumpRunning = false
    resolving.value = false
  }
}

/** Re-asks for everything still on screen, after a change that invalidated the pending work. */
function refill(lang: string) {
  queue.clear()
  for (const point of tracked.values()) enqueue(point, lang)
  void pump()
}

export function useReverseGeocode() {
  const uiSettings = useUiSettingsStore()
  language.value = uiSettings.locale
  wanted.value = uiSettings.placeNamesEnabled

  // Place names are language-specific ("Den Haag" / "The Hague"), so a locale switch invalidates
  // pending work and re-asks for everything still on screen in the new language.
  watch(
    () => uiSettings.locale,
    (locale) => {
      if (locale === language.value) return
      language.value = locale
      refill(locale)
    },
  )

  // Turning the setting off stops the queue mid-flight and hides the names already fetched;
  // turning it back on asks again for whatever is on screen, from the server's cache.
  watch(
    () => uiSettings.placeNamesEnabled,
    (on) => {
      wanted.value = on
      if (on) refill(language.value)
      else queue.clear()
    },
  )

  /** Queues coordinates for lookup; ones already cached or queued are skipped. */
  function requestPlaces(points: Iterable<GeoPoint>, precision: GeocodePrecision) {
    const lang = language.value
    let added = false
    for (const { lat, lng } of points) {
      if (!Number.isFinite(lat) || !Number.isFinite(lng)) continue
      const point: TrackedPoint = { lat, lng, precision }
      // Tracked even while switched off: it is the record of what is on screen, which is what
      // switching back on asks about.
      track(point)
      if (!enabled.value) continue

      const before = queue.size
      enqueue(point, lang)
      added ||= queue.size > before
    }

    if (added) void pump()
  }

  /** The place for a coordinate, or null while it is unknown. Reactive: fills in as answers land. */
  function placeFor(
    lat: number | null | undefined,
    lng: number | null | undefined,
    precision: GeocodePrecision,
  ): Place | null {
    if (!enabled.value || lat == null || lng == null) return null
    return places.get(keyOf(lat, lng, precision, language.value)) ?? null
  }

  return {
    requestPlaces,
    placeFor,
    /**
     * False when place names are switched off, either by the user's setting or by the
     * deployment. Callers then show their own fallback rather than a placeholder.
     */
    placeNamesEnabled: enabled,
    /** True while lookups are in flight, so a caller can show a placeholder instead of a fallback. */
    placesResolving: resolving,
  }
}
