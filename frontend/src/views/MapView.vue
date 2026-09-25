<script setup lang="ts">
import { onMounted, onUnmounted, computed, ref, watch, nextTick } from 'vue'
import { storeToRefs } from 'pinia'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { useVehicleStore } from '@/stores/vehicle'
import { useMapSettingsStore } from '@/stores/settingsMap'
import { useUiSettingsStore, DEFAULT_FILTER_DAYS } from '@/stores/settingsUi'
import { LMap, LMarker, LPopup } from '@vue-leaflet/vue-leaflet'
import ToolbarPanel from '@/components/ToolbarPanel.vue'
import SettingsToggle from '@/components/SettingsToggle.vue'
import MapLegend from '@/components/MapLegend.vue'
import { useInfiniteScroll } from '@/composables/useInfiniteScroll'
import { usePoiLayers } from '@/composables/usePoiLayers'
import { useLeafletMap } from '@/composables/useLeafletMap'
import { useBasemap } from '@/composables/useBasemap'
import { useReverseGeocode } from '@/composables/useReverseGeocode'
import { useTripMatch, type TripMatch } from '@/composables/useTripMatch'
import { addressLabel, cityName } from '@/utils/places'
import { mapAppLink } from '@/utils/mapLinks'
import { buildTripRow } from '@/utils/tripRows'
import { downsample } from '@/utils/downsample'
import { distanceWeightedSamples } from '@/utils/heatSamples'
import type { GeoPoint } from '@/services/mapApi'
import Slider from '@vueform/slider'
import Multiselect from '@vueform/multiselect'
import { L, type LeafletMap } from '@/utils/leaflet'
import { heatLayer as createHeatLayer } from '@/utils/heatLayer'
import '@/assets/map.css'
import type { Trip } from '@/services/vehicleApi'
import { buildCarMarkerIcon } from '@/utils/mapCarIcon'
import {
  speedLimitSegments,
  speedLimitSummary,
  OVER_LIMIT_TOLERANCE_KPH,
} from '@/utils/speedLimits'
import { daysAgoIso } from '@/utils/dates'
import { intlLocale } from '@/utils/format'
import { isPhoneViewport } from '@/utils/viewport'

const { t } = useI18n()
const route = useRoute()
const router = useRouter()
const store = useVehicleStore()
const settingsStore = useMapSettingsStore()
const uiSettingsStore = useUiSettingsStore()

const vin = computed(() => store.activeVin)
const status = computed(() => store.currentStatus)
const vehicleType = computed(() => store.effectiveVehicleType)
const isHev = computed(() => vehicleType.value === 'hev')
const isBev = computed(() => vehicleType.value === 'bev')
// Which of the two cars' rows either panel offers. Until vehicleType resolves from 'unknown',
// neither flag is true, so both cars' layer rows and both their filters would appear and then
// half of them be taken away again once the type is known - a visible shift in either panel.
// Staying false while unknown keeps each panel from ever showing more rows than its final,
// resolved state. The badges count off the same two, so a setting left over from another car
// never gets counted against a row this one has no way to reach.
const vehicleTypeKnown = computed(() => vehicleType.value !== 'unknown')
const carTakesFuel = computed(() => vehicleTypeKnown.value && !isBev.value)
const carTakesCharge = computed(() => vehicleTypeKnown.value && !isHev.value)
const displayLocale = computed(() => intlLocale(uiSettingsStore.locale))
const selectedTripIndex = ref<number | null>(null)
// Plain refs on their stores already (Composition-API-style defineStore) - storeToRefs gives
// directly writable, reactive bindings with no computed({get, set}) wrapper needed.
const {
  heatmapEnabled,
  speedOverlayEnabled,
  speedLimitOverlayEnabled,
  routeOutlineEnabled,
  snapToRoadsEnabled,
} = storeToRefs(settingsStore)
const { filterDays: dateRangeDays } = storeToRefs(uiSettingsStore)

let shouldSelectLatest = route.query.selectLatest === '1'
const LOAD_MORE_SIZE = 10

const mapWrapperRef = ref<HTMLElement | null>(null)
const tripSidebarRef = ref<HTMLElement | null>(null)
const { mapInstance, bindMapReady } = useLeafletMap(mapWrapperRef)

// Vector basemap in the tile pane: follows the theme and the UI language, and everything below
// draws on top of it exactly as it did over the raster tiles.
useBasemap(mapInstance)

// Charging-station / fuel-station / service-area layers: settings bindings, on-demand tile
// fetching/caching, marker clustering, and popups all live in this composable so this view only
// has to wire up the returned bindings and trigger the initial load once the map is ready.
const {
  chargingStationsEnabled,
  fuelStationsEnabled,
  serviceAreasEnabled,
  speedCamerasEnabled,
  speedCamerasAvailable,
  fuelBrandFilter,
  fuelTypeFilter,
  fuelTypeOptions,
  chargingMinPowerKw,
  chargingMaxPowerKw,
  powerRangeSlider,
  powerRangeLabel,
  formatPowerTooltip,
  availableFuelBrands,
  brandsLoading,
  poiLoading,
  loadLayers,
} = usePoiLayers({ mapInstance, vehicleType, isHev, isBev })

// ── Header badges ──────────────────────────────────────────────────────────────
// What each panel button shows over its corner, so the map can be read without opening either
// one. A filter counts when it is narrowing something rather than merely having a value, which
// for the period means differing from the one every view starts on.
const activeFilterCount = computed(() => {
  const active = [
    dateRangeDays.value !== DEFAULT_FILTER_DAYS,
    carTakesFuel.value && fuelTypeFilter.value.length > 0,
    carTakesFuel.value && fuelBrandFilter.value.length > 0,
    // One row, so one count, however many of its two ends have been moved off "any".
    carTakesCharge.value && (chargingMinPowerKw.value > 0 || chargingMaxPowerKw.value > 0),
  ]
  return active.filter(Boolean).length
})

const activeLayerCount = computed(() => {
  const active = [
    heatmapEnabled.value,
    routeOutlineEnabled.value,
    snapToRoadsEnabled.value,
    speedOverlayEnabled.value,
    // Hidden without snapping, and inert too: the limits arrive with the snapped line.
    snapToRoadsEnabled.value && speedLimitOverlayEnabled.value,
    serviceAreasEnabled.value,
    speedCamerasAvailable.value && speedCamerasEnabled.value,
    carTakesFuel.value && fuelStationsEnabled.value,
    carTakesCharge.value && chargingStationsEnabled.value,
  ]
  return active.filter(Boolean).length
})

let heatLayer: L.Layer | null = null
let routeLines: L.Polyline[] = []
let startMarker: L.Marker | null = null
let endMarker: L.Marker | null = null
let mapUpdateRaf: number | null = null
let hasCenteredOnStatus = false

const tripColors = ['#3b82f6', '#10b981', '#f59e0b', '#ef4444', '#8b5cf6', '#ec4899']

function tripColor(index: number): string {
  return tripColors[index % tripColors.length] ?? '#3b82f6'
}

function tripColorClass(index: number): string {
  return `trip-list__dot--${index % tripColors.length}`
}

const speedStops: Array<{ speed: number; r: number; g: number; b: number }> = [
  { speed: 0, r: 16, g: 185, b: 129 },
  { speed: 50, r: 132, g: 204, b: 22 },
  { speed: 90, r: 245, g: 158, b: 11 },
  { speed: 120, r: 249, g: 115, b: 22 },
  { speed: 150, r: 239, g: 68, b: 68 },
]

function speedToColor(speed: number | null, fallback: string): string {
  if (speed === null) return fallback
  const s = Math.max(0, speed)
  const last = speedStops[speedStops.length - 1]!
  if (s >= last.speed) return `rgb(${last.r},${last.g},${last.b})`
  let lo = speedStops[0]!
  let hi = last
  for (let i = 0; i < speedStops.length - 1; i++) {
    if (s >= speedStops[i]!.speed && s < speedStops[i + 1]!.speed) {
      lo = speedStops[i]!
      hi = speedStops[i + 1]!
      break
    }
  }
  const t = (s - lo.speed) / (hi.speed - lo.speed)
  const r = Math.round(lo.r + t * (hi.r - lo.r))
  const g = Math.round(lo.g + t * (hi.g - lo.g))
  const b = Math.round(lo.b + t * (hi.b - lo.b))
  return `rgb(${r},${g},${b})`
}

// Static initial centre - controlled by fitAll/flyToStatus after data loads.
const center: [number, number] = [52.3676, 4.9041]

// Trips displayed newest-first in the sidebar; selectedTripIndex is always the real store.trips index.
const newestFirstTrips = computed(() => [...store.trips].reverse())

const {
  displayItems: displayTrips,
  sentinelRef,
  reset: resetTripScroll,
  observe: observeTrips,
} = useInfiniteScroll(newestFirstTrips, LOAD_MORE_SIZE)

function realIndex(newestFirstIdx: number): number {
  return store.trips.length - 1 - newestFirstIdx
}

const allPoints = computed<[number, number][]>(() =>
  store.trips.flatMap((trip) =>
    trip.points.map((p) => [p.latitude, p.longitude] as [number, number]),
  ),
)

// The backend always appends the still-open segment as the last trip while the car hasn't
// been parked for 5+ minutes, so a positive currentJourneyDistance means that last trip is
// the one currently being driven.
const activeTripIndex = computed<number | null>(() => {
  if (store.trips.length === 0) return null
  const dist = status.value?.currentJourneyDistance
  if (dist == null || dist <= 0) return null
  return store.trips.length - 1
})

const activeCarLatLng = computed<[number, number] | null>(() => {
  const s = status.value
  if (s?.latitude != null && s?.longitude != null) return [s.latitude, s.longitude]
  const idx = activeTripIndex.value
  const trip = idx !== null ? store.trips[idx] : undefined
  const last = trip?.points[trip.points.length - 1]
  return last ? [last.latitude, last.longitude] : null
})

const activeCarHeading = computed(() => status.value?.heading ?? 0)

// ── Snapped trip lines ─────────────────────────────────────────────────────────
// Only the selected trip is snapped: it is the one drawn as a line rather than as part of the
// heatmap, and matching every trip in a 90-day range would be hundreds of requests for lines
// nobody is looking at closely.
const { requestMatch, matchFor, snapEnabled, matching: tripMatching } = useTripMatch()

const selectedTrip = computed<Trip | null>(() =>
  selectedTripIndex.value === null ? null : (store.trips[selectedTripIndex.value] ?? null),
)
const selectedMatch = computed(() => matchFor(selectedTrip.value))

/**
 * What the map says about snapping right now: that it is waiting for the matcher, or how long the
 * trip turned out to be once its fixes were put on the roads. Null while no trip is selected, or
 * when the matcher had nothing to add, so the map stays quiet about a line that is simply raw.
 */
const snapStatus = computed<{ snapping: boolean; km: number } | null>(() => {
  if (selectedTripIndex.value === null || !snapEnabled.value) return null
  const match = selectedMatch.value
  if (match) return { snapping: false, km: match.matchedKm }
  return tripMatching.value ? { snapping: true, km: 0 } : null
})

watch(
  [selectedTrip, snapEnabled],
  ([trip, enabled]) => {
    if (enabled) requestMatch(trip)
  },
  { immediate: true },
)

// ── Heatmap points ─────────────────────────────────────────────────────────────
/**
 * The lines the heat is sampled along: a trip's snapped line where one is already cached, its raw
 * fixes otherwise. Nothing extra is requested for this, so the heat follows the roads for trips
 * that have been selected at some point and cuts corners like the raw fixes do for the rest.
 */
const heatLines = computed<[number, number][][]>(() =>
  store.trips.map(
    (trip) =>
      matchFor(trip)?.coordinates ??
      trip.points.map((p) => [p.latitude, p.longitude] as [number, number]),
  ),
)

const heatPoints = computed(() => distanceWeightedSamples(heatLines.value, MAX_HEATMAP_POINTS))

// ── Speed limits ───────────────────────────────────────────────────────────────
// The limits come with the snapped line: they belong to the roads the matcher found, so there is
// nothing to show until a trip is selected and matched.
const speedLimitSummaryOfTrip = computed(() => {
  if (!speedLimitOverlayEnabled.value) return null
  const match = selectedMatch.value
  return match ? speedLimitSummary(match) : null
})

/**
 * Whether the line is drawn in limit colours. A trip whose roads OSM holds no limit for anywhere
 * would come out uniformly grey, which says less than the trip's own colour does, so it keeps
 * that and the legend says why.
 */
const limitColoursShown = computed(
  () => (speedLimitSummaryOfTrip.value?.knownKm ?? 0) > 0 && selectedMatch.value !== null,
)

const limitCoveragePct = computed(() => {
  const summary = speedLimitSummaryOfTrip.value
  if (!summary || summary.totalKm <= 0) return 0
  return Math.round((summary.knownKm / summary.totalKm) * 100)
})

const limitOverPct = computed(() => {
  const summary = speedLimitSummaryOfTrip.value
  if (!summary || summary.knownKm <= 0) return 0
  return Math.round((summary.overKm / summary.knownKm) * 100)
})

// Whichever speed key is showing opens and folds as one. A phone's map is too short to give the
// key its room by default, so there it starts folded to its button.
const legendExpanded = ref(!isPhoneViewport())

// ── Place names ────────────────────────────────────────────────────────────────
const { requestPlaces, placeFor, placeNamesEnabled, placesResolving } = useReverseGeocode()

function tripEnds(trip: Trip) {
  return { start: trip.points[0], end: trip.points[trip.points.length - 1] }
}

// Only the rows the sidebar actually shows are looked up: a 90-day range would otherwise queue
// hundreds of lookups for trips nobody has scrolled to. The list grows, so this grows with it.
// The trip being driven contributes only its origin, because its last point is the car's live
// position: asking about that as it moves would be a lookup every few hundred metres.
const visibleTripEnds = computed<GeoPoint[]>(() =>
  displayTrips.value.flatMap((trip, displayIdx) => {
    const { start, end } = tripEnds(trip)
    const wanted = realIndex(displayIdx) === activeTripIndex.value ? [start] : [start, end]
    return wanted
      .filter((p): p is NonNullable<typeof p> => p != null)
      .map((p) => ({ lat: p.latitude, lng: p.longitude }))
  }),
)

watch(visibleTripEnds, (points) => requestPlaces(points, 'city'), { immediate: true })

// The car's own street and city, for its popup. Only while parked: on the move this would ask
// for a new address on every position update, and name a street the car has already left.
const parkedCarLatLng = computed<GeoPoint | null>(() => {
  const s = status.value
  if (activeTripIndex.value !== null) return null
  return s?.latitude != null && s?.longitude != null ? { lat: s.latitude, lng: s.longitude } : null
})

watch(
  parkedCarLatLng,
  (point) => {
    if (point) requestPlaces([point], 'address')
  },
  { immediate: true },
)

const carAddress = computed(() =>
  addressLabel(placeFor(parkedCarLatLng.value?.lat, parkedCarLatLng.value?.lng, 'address')),
)

// Getting to the car is a job for whatever can navigate: a map app on a phone, openstreetmap.org
// on a desktop. The popup offers whichever of the two this device can actually open.
const carMapLink = computed(() => {
  const s = status.value
  if (s?.latitude == null || s?.longitude == null) return null
  return mapAppLink(s.latitude, s.longitude, store.activeVehicle?.model ?? undefined)
})

// One row model per visible trip, paired with the store index selection works on.
const displayRows = computed(() =>
  displayTrips.value.map((trip, displayIdx) => {
    const { start, end } = tripEnds(trip)
    const realIdx = realIndex(displayIdx)
    return {
      realIdx,
      row: buildTripRow(trip, {
        fromCity: cityName(placeFor(start?.latitude, start?.longitude, 'city')),
        toCity: cityName(placeFor(end?.latitude, end?.longitude, 'city')),
        inProgress: realIdx === activeTripIndex.value,
        canResolve: start != null && end != null && placeNamesEnabled.value,
        resolving: placesResolving.value,
        locale: displayLocale.value,
        t,
      }),
    }
  }),
)

function clearRouteLines() {
  routeLines.forEach((l) => l.remove())
  routeLines = []
  if (startMarker) {
    startMarker.remove()
    startMarker = null
  }
  if (endMarker) {
    endMarker.remove()
    endMarker = null
  }
}

function buildHeatLayer() {
  const map = mapInstance.value
  if (!map || heatPoints.value.length === 0 || !heatmapEnabled.value) return
  const layer = createHeatLayer(heatPoints.value, {
    radius: 18,
    blur: 22,
    maxZoom: 17,
    gradient: { 0.4: '#3b82f6', 0.65: '#f59e0b', 1.0: '#ef4444' },
  })
  if (!layer) {
    console.warn('[map] leaflet.heat plugin not available')
    return
  }
  if (heatLayer) heatLayer.remove()
  heatLayer = layer.addTo(map)
}

function buildRouteLines() {
  const map = mapInstance.value
  if (!map) return
  clearRouteLines()
  store.trips.forEach((trip, i) => {
    const pts = trip.points.map((p) => [p.latitude, p.longitude] as [number, number])
    if (pts.length < 2) return
    if (routeOutlineEnabled.value) {
      const border = L.polyline(pts, { color: '#111', weight: 7, opacity: 0.4 })
      border.on('click', () => selectTrip(i))
      border.addTo(map)
      routeLines.push(border)
    }
    const line = L.polyline(pts, { color: tripColor(i), weight: 3, opacity: 0.75 })
    line.on('click', () => selectTrip(i))
    line.addTo(map)
    routeLines.push(line)
  })
}

// Speed overlay renders one Leaflet polyline layer per segment. A long trip can have thousands
// of GPS points, which would create thousands of DOM elements - downsample first so the map
// stays responsive; a few hundred segments is already more color resolution than is visible.
const MAX_SPEED_OVERLAY_SEGMENTS = 500

// How many samples the heatmap is drawn from. It renders to a canvas rather than one DOM element
// per point, so it tolerates far more than the polyline overlay, but a 90-day range still has to
// stay quick to rebuild. The spacing between samples follows from this budget and how far the
// trips run in total, so a longer period draws coarser instead of slower.
const MAX_HEATMAP_POINTS = 5000

interface SpeedSegment {
  coordinates: [number, number][]
  speed: number | null
}

/**
 * The stretches the speed overlay colours. Without a snapped line that is one straight stretch
 * per pair of fixes, as it always was; with one, each stretch follows the road between the two
 * fixes it spans, so the colours sit on the route that was driven rather than on the line cutting
 * across it.
 */
function speedSegments(trip: Trip, match: TripMatch | null): SpeedSegment[] {
  const segments: SpeedSegment[] = []

  if (!match) {
    const pts = downsample(trip.points, MAX_SPEED_OVERLAY_SEGMENTS)
    for (let i = 0; i < pts.length - 1; i++) {
      const from = pts[i]!
      const to = pts[i + 1]!
      segments.push({
        coordinates: [
          [from.latitude, from.longitude],
          [to.latitude, to.longitude],
        ],
        speed: from.speed,
      })
    }
    return segments
  }

  // Pair every sent fix with its vertex on the snapped line before thinning, so a fix that
  // survives the thinning takes its place on that line with it.
  const placed = match.points.map((point, i) => ({ point, index: match.pointIndexes[i] ?? 0 }))
  const thinned = downsample(placed, MAX_SPEED_OVERLAY_SEGMENTS)

  let cursor = thinned[0]?.index ?? 0
  for (let i = 0; i < thinned.length - 1; i++) {
    const next = thinned[i + 1]!.index
    // Fixes the matcher could not place share a vertex with their neighbour. Carrying the cursor
    // past them keeps the coloured line continuous instead of leaving gaps in it.
    if (next <= cursor) continue
    segments.push({
      coordinates: match.coordinates.slice(cursor, next + 1),
      speed: thinned[i]!.point.speed,
    })
    cursor = next
  }

  // Whatever the snapped line runs on past the last fix still belongs to the trip.
  if (cursor < match.coordinates.length - 1) {
    segments.push({
      coordinates: match.coordinates.slice(cursor),
      speed: thinned[thinned.length - 1]?.point.speed ?? null,
    })
  }

  return segments
}

function buildSelectedLine() {
  const map = mapInstance.value
  const idx = selectedTripIndex.value
  if (!map || idx === null) return
  clearRouteLines()
  const trip = store.trips[idx]
  if (!trip) return
  const pts = trip.points
  if (pts.length < 2) return

  // The snapped line when there is one, the raw fixes until then: the map never waits on the
  // matcher, it redraws once the answer lands.
  const match = selectedMatch.value
  const coords = match?.coordinates ?? pts.map((p) => [p.latitude, p.longitude] as [number, number])

  if (routeOutlineEnabled.value) {
    const border = L.polyline(coords, { color: '#111', weight: 9, opacity: 0.4 })
    border.addTo(map)
    routeLines.push(border)
  }

  if (limitColoursShown.value && match) {
    // Colour follows the road rather than the fixes here: one layer per stretch that shares a
    // verdict, so a trip is a handful of lines instead of one per vertex.
    for (const { coordinates, state } of speedLimitSegments(match)) {
      const segment = L.polyline(coordinates, {
        className: `trip-limit-line trip-limit-line--${state}`,
        weight: 5,
        opacity: 1,
        lineCap: 'square',
      })
      segment.addTo(map)
      routeLines.push(segment)
    }
  } else if (speedOverlayEnabled.value) {
    for (const { coordinates, speed } of speedSegments(trip, match)) {
      const segment = L.polyline(coordinates, {
        color: speedToColor(speed, tripColor(idx)),
        weight: 5,
        opacity: 1,
        lineCap: 'square',
      })
      segment.addTo(map)
      routeLines.push(segment)
    }
  } else {
    const line = L.polyline(coords, { color: tripColor(idx), weight: 5, opacity: 1 })
    line.addTo(map)
    routeLines.push(line)
  }

  buildTripMarkers(trip, idx)
}

function buildTripMarkers(trip: Trip, realIdx: number) {
  const map = mapInstance.value
  if (!map || trip.points.length === 0) return

  const start = trip.points[0]
  if (!start) return

  const startIcon = L.divIcon({
    className: '',
    html: '<div class="trip-marker trip-marker--start"></div>',
    iconSize: [16, 16],
    iconAnchor: [8, 8],
  })
  startMarker = L.marker([start.latitude, start.longitude], { icon: startIcon }).addTo(map)

  if (realIdx === activeTripIndex.value && activeCarLatLng.value) {
    endMarker = L.marker(activeCarLatLng.value, {
      icon: buildCarMarkerIcon(activeCarHeading.value),
    }).addTo(map)
    return
  }

  const end = trip.points[trip.points.length - 1]
  if (!end) return

  const endIcon = L.divIcon({
    className: '',
    html: '<div class="trip-marker trip-marker--end"><div class="trip-flag-pole"></div><div class="trip-flag-flag"></div></div>',
    iconSize: [20, 32],
    iconAnchor: [2, 32],
  })
  endMarker = L.marker([end.latitude, end.longitude], { icon: endIcon }).addTo(map)
}

function removeHeatLayer() {
  if (heatLayer) {
    heatLayer.remove()
    heatLayer = null
  }
}

function fitBoundsSafe(pts: [number, number][]) {
  const map = mapInstance.value
  if (!map || pts.length === 0) return
  const bounds = L.latLngBounds(pts)
  if (bounds.getNorthEast().equals(bounds.getSouthWest())) {
    map.setView(bounds.getCenter(), 15, { animate: false })
  } else {
    map.fitBounds(bounds, { padding: [32, 32], animate: false })
  }
}

function fitAll() {
  if (allPoints.value.length > 0) {
    fitBoundsSafe(allPoints.value)
  } else if (status.value?.latitude != null && status.value?.longitude != null) {
    mapInstance.value?.setView([status.value.latitude, status.value.longitude], 14, {
      animate: false,
    })
  }
}

function fitTrip(trip: Trip) {
  fitBoundsSafe(trip.points.map((p) => [p.latitude, p.longitude] as [number, number]))
}

function flyToStatus() {
  const map = mapInstance.value
  const s = status.value
  if (map && s?.latitude != null && s?.longitude != null) {
    selectedTripIndex.value = null
    map.setView([s.latitude, s.longitude], 14, { animate: false })
  }
}

function onMapReady(map: LeafletMap) {
  // POI layer reload on pan/zoom is handled internally by usePoiLayers (it watches mapInstance).
  bindMapReady(map, () => {
    if (allPoints.value.length > 0) {
      buildHeatLayer()
      buildRouteLines()
      fitAll()
    } else if (status.value?.latitude != null && status.value?.longitude != null) {
      hasCenteredOnStatus = true
      map.setView([status.value.latitude, status.value.longitude], 14, { animate: false })
    }
    loadLayers(100)
  })
}

// Center on car position only once on initial load. Subsequent status updates (SignalR
// reconnects every ~60s) must not move the map away from where the user is looking.
watch(status, (s) => {
  if (!mapInstance.value || s?.latitude == null || s?.longitude == null) return
  if (!hasCenteredOnStatus && allPoints.value.length === 0) {
    hasCenteredOnStatus = true
    mapInstance.value.setView([s.latitude, s.longitude], 14, { animate: false })
  }
})

// Keep the active trip's car marker following the live position/heading while it's the
// selected trip, without a full rebuild (would reset the speed-overlay/route-outline state).
watch(status, (s) => {
  if (!endMarker || selectedTripIndex.value === null) return
  if (selectedTripIndex.value !== activeTripIndex.value) return
  if (s?.latitude == null || s?.longitude == null) return
  endMarker.setLatLng([s.latitude, s.longitude])
  endMarker.setIcon(buildCarMarkerIcon(s.heading ?? 0))
})

// When trips load after the map is ready, rebuild layers and fit
watch(allPoints, async (pts) => {
  if (pts.length === 0 || !mapInstance.value) return
  await nextTick()
  buildHeatLayer()
  buildRouteLines()
  if (shouldSelectLatest) {
    shouldSelectLatest = false
    selectTrip(store.trips.length - 1)
  } else {
    fitAll()
  }
})

// Leaflet operations are deferred to the next animation frame so the Vue DOM
// update (active state CSS transition, popover enter) renders cleanly before
// the canvas GPU layer is torn down and rebuilt.
function scheduleMapUpdate(update: () => void) {
  if (mapUpdateRaf !== null) cancelAnimationFrame(mapUpdateRaf)
  mapUpdateRaf = requestAnimationFrame(() => {
    mapUpdateRaf = null
    update()
  })
}

// Trip selection drives map display and popover position.
watch(selectedTripIndex, (idx) => {
  scheduleMapUpdate(() => {
    if (idx === null) {
      if (heatmapEnabled.value) buildHeatLayer()
      buildRouteLines()
      fitAll()
    } else {
      removeHeatLayer()
      buildSelectedLine()
      const trip = store.trips[idx]
      if (trip) fitTrip(trip)
    }
  })
})

// Heatmap toggle while no trip is selected
watch(heatmapEnabled, (enabled) => {
  if (selectedTripIndex.value !== null) return
  if (enabled) buildHeatLayer()
  else removeHeatLayer()
})

// Speed overlay toggle while a trip is selected. Only one of the two overlays can colour the same
// line, so switching one on switches the other off rather than letting one quietly win.
watch(speedOverlayEnabled, (on) => {
  if (on) speedLimitOverlayEnabled.value = false
  if (selectedTripIndex.value === null) return
  scheduleMapUpdate(buildSelectedLine)
})

watch(speedLimitOverlayEnabled, (on) => {
  if (on) speedOverlayEnabled.value = false
  if (selectedTripIndex.value === null) return
  scheduleMapUpdate(buildSelectedLine)
})

// Route outline toggle: rebuild whichever layer is currently active
watch(routeOutlineEnabled, () => {
  scheduleMapUpdate(() => {
    if (selectedTripIndex.value === null) buildRouteLines()
    else buildSelectedLine()
  })
})

// The snapped line lands a moment after the trip was drawn from its raw fixes, and disappears
// again when snapping is switched off: either way the selected trip is redrawn in place.
watch(selectedMatch, () => {
  if (selectedTripIndex.value === null) return
  scheduleMapUpdate(buildSelectedLine)
})

// Date range change: reload trips and reset state
watch(dateRangeDays, async (days) => {
  resetTripScroll()
  selectedTripIndex.value = null
  if (vin.value) {
    await store.fetchTrips(vin.value, daysAgoIso(days))
  }
})

// Trip just finished: silently prepend without resetting selection or page
watch(
  () => store.tripJustCompleted,
  async (completed) => {
    if (!completed || !vin.value) return
    await store.fetchTrips(vin.value, daysAgoIso(dateRangeDays.value))
  },
)

function selectTrip(realIdx: number) {
  if (selectedTripIndex.value === realIdx) {
    selectedTripIndex.value = null
    return
  }
  selectedTripIndex.value = realIdx
  nextTick(() => {
    const sidebar = tripSidebarRef.value
    const active = sidebar?.querySelector('.trip-list__item--active') as HTMLElement | null
    if (!sidebar || !active) return
    const header = sidebar.querySelector('.trip-sidebar__header') as HTMLElement | null
    const headerHeight = header?.offsetHeight ?? 0
    const sidebarRect = sidebar.getBoundingClientRect()
    const itemRect = active.getBoundingClientRect()
    const itemTop = itemRect.top - sidebarRect.top
    const itemBottom = itemRect.bottom - sidebarRect.top
    if (itemTop < headerHeight) {
      sidebar.scrollBy({ top: itemTop - headerHeight, behavior: 'smooth' })
    } else if (itemBottom > sidebarRect.height) {
      sidebar.scrollBy({ top: itemBottom - sidebarRect.height, behavior: 'smooth' })
    }
  })
}

onMounted(async () => {
  if (route.query.selectLatest) {
    router.replace({ name: 'map' })
  }
  await store.fetchVehicles()
  if (vin.value) {
    await Promise.all([
      store.fetchStatus(vin.value),
      store.fetchConfig(vin.value),
      store.fetchTrips(vin.value, daysAgoIso(dateRangeDays.value)),
    ])
  }
  nextTick(() => {
    observeTrips(tripSidebarRef.value)
  })
})

onUnmounted(() => {
  if (mapUpdateRaf !== null) cancelAnimationFrame(mapUpdateRaf)
  removeHeatLayer()
  // POI layer cleanup (debouncers, marker clearing) is handled internally by usePoiLayers.
})
</script>

<template>
  <div class="view-container view-container--map">
    <div class="view-header">
      <h1>{{ t('nav.map') }}</h1>
      <div class="view-header__actions">
        <!-- Filters narrow what the map draws; the layer panel beside it decides what it draws
             at all. A filter belongs to what the car can use rather than to what is switched on
             right now, so these stay put whether or not their layer is showing - the panel would
             otherwise look half empty until the layer was found in the other one. -->
        <ToolbarPanel :count="activeFilterCount">
          <div class="settings-toggle">
            <div class="settings-toggle__info">
              <span class="settings-toggle__label">
                <font-awesome-icon icon="calendar-check" class="settings-toggle__icon" />
                {{ t('trips.dateRange') }}
              </span>
              <span class="settings-toggle__desc">{{ t('trips.dateRangeDesc') }}</span>
            </div>
            <div class="settings-toggle__control">
              <select v-model="dateRangeDays" class="form-select form-select-sm">
                <option :value="7">{{ t('trips.last7days') }}</option>
                <option :value="30">{{ t('trips.last30days') }}</option>
                <option :value="90">{{ t('trips.last90days') }}</option>
              </select>
            </div>
          </div>
          <template v-if="carTakesFuel">
            <div class="poi-filter">
              <div class="poi-filter__header">
                <div class="settings-toggle__info">
                  <span class="settings-toggle__label">
                    <font-awesome-icon icon="gas-pump" class="settings-toggle__icon" />
                    {{ t('trips.fuelTypeFilter') }}
                  </span>
                  <span class="settings-toggle__desc">{{ t('trips.fuelTypeFilterDesc') }}</span>
                </div>
              </div>
              <Multiselect
                v-model="fuelTypeFilter"
                :options="fuelTypeOptions"
                :placeholder="t('trips.fuelTypePlaceholder')"
                :searchable="false"
                :close-on-select="false"
                :clear-on-select="false"
                mode="tags"
                :no-results-text="t('trips.fuelTypeNoMatch')"
                append-to="body"
                class="fuel-brand-multiselect"
              />
            </div>
            <div class="poi-filter">
              <div class="poi-filter__header">
                <div class="settings-toggle__info">
                  <span class="settings-toggle__label">
                    <font-awesome-icon icon="tag" class="settings-toggle__icon" />
                    {{ t('trips.fuelBrandFilter') }}
                  </span>
                  <span class="settings-toggle__desc">{{ t('trips.fuelBrandFilterDesc') }}</span>
                </div>
              </div>
              <Multiselect
                v-model="fuelBrandFilter"
                :options="availableFuelBrands"
                :placeholder="t('trips.fuelBrandPlaceholder')"
                :searchable="true"
                :close-on-select="false"
                :clear-on-select="false"
                mode="tags"
                :loading="brandsLoading"
                :no-results-text="t('trips.fuelBrandNoMatch')"
                :no-options-text="t('trips.fuelBrandNoneLoaded')"
                append-to="body"
                class="fuel-brand-multiselect"
              />
            </div>
          </template>
          <template v-if="carTakesCharge">
            <div class="charging-power-filter">
              <div class="charging-power-filter__header">
                <div>
                  <span class="settings-toggle__label">
                    <font-awesome-icon icon="bolt" class="settings-toggle__icon" />
                    {{ t('trips.chargingPower') }}
                  </span>
                  <span class="settings-toggle__desc">{{ t('trips.chargingPowerDesc') }}</span>
                </div>
                <span class="charging-power-filter__range">{{ powerRangeLabel }}</span>
              </div>
              <div class="charging-power-filter__slider">
                <Slider
                  v-model="powerRangeSlider"
                  :min="0"
                  :max="350"
                  :step="10"
                  :tooltips="true"
                  :format="formatPowerTooltip"
                  :merge="50"
                  :lazy="false"
                  class="charging-slider"
                  :aria-label="[t('trips.chargingMinPower'), t('trips.chargingMaxPower')]"
                />
              </div>
            </div>
          </template>
        </ToolbarPanel>
        <ToolbarPanel :title="t('common.layers')" icon="layer-group" :count="activeLayerCount">
          <SettingsToggle v-model="heatmapEnabled" :label="t('trips.heatmap')">
            <template #label>
              <span class="settings-toggle__label">
                <font-awesome-icon icon="fire" class="settings-toggle__icon" />
                {{ t('trips.heatmap') }}
              </span>
              <span class="settings-toggle__desc">{{ t('trips.heatmapDesc') }}</span>
            </template>
          </SettingsToggle>
          <SettingsToggle v-model="routeOutlineEnabled" :label="t('trips.routeOutline')">
            <template #label>
              <span class="settings-toggle__label">
                <font-awesome-icon icon="route" class="settings-toggle__icon" />
                {{ t('trips.routeOutline') }}
              </span>
              <span class="settings-toggle__desc">{{ t('trips.routeOutlineDesc') }}</span>
            </template>
          </SettingsToggle>
          <SettingsToggle v-model="snapToRoadsEnabled" :label="t('trips.snapToRoads')">
            <template #label>
              <span class="settings-toggle__label">
                <font-awesome-icon icon="road-circle-check" class="settings-toggle__icon" />
                {{ t('trips.snapToRoads') }}
              </span>
              <span class="settings-toggle__desc">{{ t('trips.snapToRoadsDesc') }}</span>
            </template>
          </SettingsToggle>
          <SettingsToggle v-model="speedOverlayEnabled" :label="t('trips.speedOverlay')">
            <template #label>
              <span class="settings-toggle__label">
                <font-awesome-icon icon="gauge" class="settings-toggle__icon" />
                {{ t('trips.speedOverlay') }}
              </span>
              <span class="settings-toggle__desc">{{ t('trips.speedOverlayDesc') }}</span>
            </template>
          </SettingsToggle>
          <!-- The limits ride along with the snapped line, so without snapping there is nothing
               to colour the trip against. -->
          <SettingsToggle
            v-if="snapToRoadsEnabled"
            v-model="speedLimitOverlayEnabled"
            :label="t('trips.speedLimitOverlay')"
          >
            <template #label>
              <span class="settings-toggle__label">
                <font-awesome-icon icon="gauge-high" class="settings-toggle__icon" />
                {{ t('trips.speedLimitOverlay') }}
              </span>
              <span class="settings-toggle__desc">{{ t('trips.speedLimitOverlayDesc') }}</span>
            </template>
          </SettingsToggle>
          <SettingsToggle v-model="serviceAreasEnabled" :label="t('trips.serviceAreas')">
            <template #label>
              <span class="settings-toggle__label">
                <font-awesome-icon icon="road" class="settings-toggle__icon" />
                {{ t('trips.serviceAreas') }}
              </span>
              <span class="settings-toggle__desc">{{ t('trips.serviceAreasDesc') }}</span>
            </template>
          </SettingsToggle>
          <!-- Left out entirely where the deployment does not serve the layer, which is how a
               jurisdiction that restricts flagging camera positions switches it off. -->
          <SettingsToggle
            v-if="speedCamerasAvailable"
            v-model="speedCamerasEnabled"
            :label="t('trips.speedCameras')"
          >
            <template #label>
              <span class="settings-toggle__label">
                <font-awesome-icon icon="camera" class="settings-toggle__icon" />
                {{ t('trips.speedCameras') }}
              </span>
              <span class="settings-toggle__desc">{{ t('trips.speedCamerasDesc') }}</span>
            </template>
          </SettingsToggle>
          <SettingsToggle
            v-if="carTakesFuel"
            v-model="fuelStationsEnabled"
            :label="t('trips.fuelStations')"
          >
            <template #label>
              <span class="settings-toggle__label">
                <font-awesome-icon icon="gas-pump" class="settings-toggle__icon" />
                {{ t('trips.fuelStations') }}
              </span>
              <span class="settings-toggle__desc">{{ t('trips.fuelStationsDesc') }}</span>
            </template>
          </SettingsToggle>
          <SettingsToggle
            v-if="carTakesCharge"
            v-model="chargingStationsEnabled"
            :label="t('trips.chargingStations')"
          >
            <template #label>
              <span class="settings-toggle__label">
                <font-awesome-icon icon="bolt" class="settings-toggle__icon" />
                {{ t('trips.chargingStations') }}
              </span>
              <span class="settings-toggle__desc">{{ t('trips.chargingStationsDesc') }}</span>
            </template>
          </SettingsToggle>
        </ToolbarPanel>
        <button
          class="btn btn-sm btn-outline-secondary"
          :disabled="status?.latitude == null || status?.longitude == null"
          @click="flyToStatus"
        >
          <font-awesome-icon icon="location-dot" />
          {{ t('trips.findCar') }}
        </button>
      </div>
    </div>

    <div class="map-layout">
      <!-- Trip sidebar -->
      <aside ref="tripSidebarRef" class="trip-sidebar">
        <div class="trip-sidebar__header">
          <h2 class="trip-sidebar__title">{{ t('trips.title') }}</h2>
        </div>

        <div v-if="store.loading" class="trip-list">
          <div v-for="i in LOAD_MORE_SIZE" :key="i" class="trip-list__item">
            <span class="trip-list__dot skeleton" />
            <div class="trip-list__info">
              <div class="trip-list__header">
                <span class="skeleton skeleton--text skeleton--text-lg" />
                <span class="skeleton skeleton--trip-time" />
              </div>
              <span class="skeleton skeleton--text skeleton--text-md" />
            </div>
          </div>
        </div>

        <div v-else-if="!store.trips.length" class="empty-state text-sm">
          {{ t('trips.noTrips') }}
        </div>

        <ul v-else class="trip-list">
          <li
            v-for="{ row, realIdx } in displayRows"
            :key="realIdx"
            class="trip-list__item"
            :class="{ 'trip-list__item--active': selectedTripIndex === realIdx }"
            @click="selectTrip(realIdx)"
          >
            <span
              class="trip-list__dot"
              :class="[
                tripColorClass(realIdx),
                { 'trip-list__dot--live': realIdx === activeTripIndex },
              ]"
            />
            <div class="trip-list__info">
              <div class="trip-list__header">
                <span
                  v-if="row.mode === 'route'"
                  class="trip-list__name trip-list__route"
                  :title="row.routeTitle"
                >
                  <span class="trip-list__place">{{ row.from }}</span>
                  <template v-if="row.to">
                    <font-awesome-icon icon="arrow-right" class="trip-list__route-arrow" />
                    <span class="trip-list__place">{{ row.to }}</span>
                  </template>
                </span>
                <span
                  v-else-if="row.mode === 'pending'"
                  class="skeleton skeleton--text skeleton--text-lg trip-list__name-skeleton"
                  role="status"
                  :aria-label="t('trips.resolvingPlaces')"
                />
                <span v-else class="trip-list__name" :title="row.dateLabel">{{
                  row.dateLabel
                }}</span>
                <span v-if="realIdx === activeTripIndex" class="trip-list__live-badge">{{
                  t('trips.inProgress')
                }}</span>
                <span v-else class="trip-list__time">{{ row.timeLabel }}</span>
              </div>
              <span class="trip-list__meta" :title="row.metaTitle">{{ row.meta }}</span>
            </div>
          </li>
        </ul>
        <div ref="sentinelRef" />
      </aside>

      <!-- Map -->
      <div ref="mapWrapperRef" class="map-wrapper">
        <LMap :zoom="13" :center="center" class="map-canvas" @ready="onMapReady">
          <!-- Current position marker: hidden while a trip is selected -->
          <LMarker
            v-if="
              status?.latitude != null && status?.longitude != null && selectedTripIndex === null
            "
            :lat-lng="[status.latitude!, status.longitude!]"
          >
            <LPopup>
              <strong class="car-popup__title">{{
                store.activeVehicle?.model ?? store.activeVin
              }}</strong>
              <span v-if="carAddress" class="car-popup__address">{{ carAddress }}</span>
              <a
                v-if="carMapLink"
                class="car-popup__link"
                :href="carMapLink.href"
                :target="carMapLink.external ? '_blank' : undefined"
                :rel="carMapLink.external ? 'noreferrer' : undefined"
              >
                <font-awesome-icon icon="diamond-turn-right" />
                {{ carMapLink.external ? t('trips.showOnOsm') : t('trips.openInMapApp') }}
              </a>
            </LPopup>
          </LMarker>
        </LMap>

        <div v-if="poiLoading || snapStatus" class="map-pill-stack">
          <div v-if="poiLoading" class="map-pill" role="status" :aria-label="t('trips.poiLoading')">
            <span class="spinner-border spinner-border-sm" aria-hidden="true" />
            {{ t('trips.poiLoading') }}
          </div>
          <div v-if="snapStatus" class="map-pill" role="status">
            <span
              v-if="snapStatus.snapping"
              class="spinner-border spinner-border-sm"
              aria-hidden="true"
            />
            <font-awesome-icon v-else icon="road-circle-check" />
            {{
              snapStatus.snapping
                ? t('trips.snapping')
                : t('trips.snapped', { km: snapStatus.km.toFixed(1) })
            }}
          </div>
        </div>

        <MapLegend
          v-if="speedOverlayEnabled && selectedTripIndex !== null"
          v-model:expanded="legendExpanded"
          :label="t('trips.speedOverlay')"
        >
          <div class="speed-legend__bar"></div>
          <div class="speed-legend__labels">
            <span>0</span>
            <span>50</span>
            <span>90</span>
            <span>130+ km/h</span>
          </div>
        </MapLegend>

        <MapLegend
          v-if="speedLimitSummaryOfTrip"
          v-model:expanded="legendExpanded"
          :label="t('trips.speedLimitOverlay')"
        >
          <template v-if="limitColoursShown">
            <div class="speed-legend__keys">
              <span class="speed-legend__key">
                <span class="speed-legend__swatch speed-legend__swatch--under"></span>
                {{ t('trips.speedLimitUnder') }}
              </span>
              <span class="speed-legend__key">
                <span class="speed-legend__swatch speed-legend__swatch--over"></span>
                {{ t('trips.speedLimitOver') }}
              </span>
              <span class="speed-legend__key">
                <span class="speed-legend__swatch speed-legend__swatch--unknown"></span>
                {{ t('trips.speedLimitUnknown') }}
              </span>
            </div>
            <div
              class="speed-legend__summary"
              :title="t('trips.speedLimitTolerance', { kph: OVER_LIMIT_TOLERANCE_KPH })"
            >
              <span v-if="speedLimitSummaryOfTrip.overKm > 0">
                {{
                  t('trips.speedLimitOverSummary', {
                    km: speedLimitSummaryOfTrip.overKm.toFixed(1),
                    pct: limitOverPct,
                    max: speedLimitSummaryOfTrip.maxOverKph,
                  })
                }}
              </span>
              <span v-else>{{ t('trips.speedLimitNoneOver') }}</span>
              <span class="speed-legend__coverage">
                {{ t('trips.speedLimitCoverage', { pct: limitCoveragePct }) }}
              </span>
            </div>
          </template>
          <div v-else class="speed-legend__summary">{{ t('trips.speedLimitUnmapped') }}</div>
        </MapLegend>
      </div>
    </div>
  </div>
</template>

<!-- Global on purpose: Leaflet builds marker and popup HTML itself, outside Vue's renderer,
     so those elements never carry a scope attribute. Everything the template owns is scoped. -->
<style>
@import url('@vueform/slider/themes/default.css');

.trip-marker--start {
  width: 16px;
  height: 16px;
  background: #10b981;
  border: 3px solid #fff;
  border-radius: 50%;
  box-shadow: 0 1px 4px rgb(0 0 0 / 35%);
  animation: trip-start-pulse 2s ease-in-out infinite;
}

@keyframes trip-start-pulse {
  0% {
    box-shadow:
      0 0 0 0 rgb(16 185 129 / 55%),
      0 1px 4px rgb(0 0 0 / 35%);
  }

  70% {
    box-shadow:
      0 0 0 10px rgb(16 185 129 / 0%),
      0 1px 4px rgb(0 0 0 / 35%);
  }

  100% {
    box-shadow:
      0 0 0 0 rgb(16 185 129 / 0%),
      0 1px 4px rgb(0 0 0 / 35%);
  }
}

.trip-marker--end {
  position: relative;
  width: 20px;
  height: 32px;
  pointer-events: none;
}

.trip-flag-pole {
  position: absolute;
  left: 1px;
  top: 0;
  width: 2px;
  height: 32px;
  background: #333;
  border-radius: 1px;
}

.trip-flag-flag {
  position: absolute;
  left: 3px;
  top: 1px;
  width: 16px;
  height: 11px;
  background: repeating-conic-gradient(#111 0% 25%, #fff 0% 50%) 0 0 / 5.33px 5.5px;
  border: 1px solid rgb(0 0 0 / 40%);
  transform-origin: left center;
  animation: trip-flag-wave 1.6s ease-in-out infinite;
}

@keyframes trip-flag-wave {
  0%,
  100% {
    transform: skewY(0deg) scaleX(1);
  }

  30% {
    transform: skewY(-3deg) scaleX(0.97);
  }

  70% {
    transform: skewY(3deg) scaleX(0.97);
  }
}
</style>

<style scoped>
.trip-list__dot--live {
  animation: trip-start-pulse 2s ease-in-out infinite;
}

.trip-list__live-badge {
  font-size: 0.75rem;
  font-weight: 600;
  color: var(--color-success, #10b981);

  /* Never squeezed by a long route headline beside it; the city names truncate instead. */
  flex-shrink: 0;
  white-space: nowrap;
}

.charging-power-filter {
  padding: 0.25rem 0 0.5rem;
}

.charging-power-filter__header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 0.9rem;
}

.charging-power-filter__range {
  font-size: 0.75rem;
  color: var(--color-text-muted, #888);
}

.charging-power-filter__slider {
  padding: 0 0.5rem;
}

.charging-slider {
  --slider-connect-bg: #22c55e;
  --slider-tooltip-bg: #22c55e;
  --slider-tooltip-color: #fff;
  --slider-handle-ring-color: rgb(34 197 94 / 20%);
}

/* Multiselect theme vars, dropdown z-index, and search input styling now live globally in
   main.css (moved there so the AppFooter settings-modal notification-type multiselect is
   themed correctly even before this lazy-loaded view has ever been visited). */

/* Shared by the fuel-type and brand filters, which are the same block with a different list. */
.poi-filter {
  padding: 0.25rem 0 0.5rem;
}

.poi-filter__header {
  margin-bottom: 0.5rem;
}
</style>
