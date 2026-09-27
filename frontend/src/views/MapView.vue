<script setup lang="ts">
import { onMounted, computed, ref, watch, nextTick, useTemplateRef } from 'vue'
import { storeToRefs } from 'pinia'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { useVehicleStore } from '@/stores/vehicle'
import { useMapSettingsStore } from '@/stores/settingsMap'
import { useUiSettingsStore } from '@/stores/settingsUi'
import { LMap, LMarker, LPopup } from '@vue-leaflet/vue-leaflet'
import MapFilterPanel from '@/components/map/MapFilterPanel.vue'
import MapLayerPanel from '@/components/map/MapLayerPanel.vue'
import TripSidebar from '@/components/map/TripSidebar.vue'
import SpeedLegend from '@/components/map/SpeedLegend.vue'
import SpeedLimitLegend from '@/components/map/SpeedLimitLegend.vue'
import { useInfiniteScroll } from '@/composables/useInfiniteScroll'
import { usePoiLayers } from '@/composables/usePoiLayers'
import { useLeafletMap } from '@/composables/useLeafletMap'
import { useBasemap } from '@/composables/useBasemap'
import { useReverseGeocode } from '@/composables/useReverseGeocode'
import { useTripMatch } from '@/composables/useTripMatch'
import { useTripLayers } from '@/composables/useTripLayers'
import { addressLabel, cityName } from '@/utils/places'
import { mapAppLink } from '@/utils/mapLinks'
import { buildTripRow } from '@/utils/tripRows'
import { distanceWeightedSamples } from '@/utils/heatSamples'
import type { GeoPoint } from '@/services/mapApi'
import { DEFAULT_MAP_CENTER, type LeafletMap } from '@/utils/leaflet'
import '@/assets/map.css'
import type { Trip } from '@/services/vehicleApi'
import { speedLimitSummary } from '@/utils/speedLimits'
import { daysAgoIso } from '@/utils/dates'
import { intlLocale } from '@/utils/format'
import { isPhoneViewport } from '@/utils/viewport'
import { useUnits } from '@/composables/useUnits'
import { burnsFuel, plugsIn } from '@/utils/vehicleType'

const { t } = useI18n()
const units = useUnits()
const route = useRoute()
const router = useRouter()
const store = useVehicleStore()
const settingsStore = useMapSettingsStore()
const uiSettingsStore = useUiSettingsStore()

const vin = computed(() => store.activeVin)
const status = computed(() => store.currentStatus)
const trips = computed(() => store.trips)
const vehicleType = computed(() => store.effectiveVehicleType)
// Which of the two cars' rows either panel offers. Until vehicleType resolves from 'unknown',
// neither flag is true, so both cars' layer rows and both their filters would appear and then
// half of them be taken away again once the type is known - a visible shift in either panel.
// Staying false while unknown keeps each panel from ever showing more rows than its final,
// resolved state. The badges count off the same two, so a setting left over from another car
// never gets counted against a row this one has no way to reach.
const carTakesFuel = computed(() => burnsFuel(vehicleType.value))
const carTakesCharge = computed(() => plugsIn(vehicleType.value))
const displayLocale = computed(() => intlLocale(uiSettingsStore.locale))
const selectedTripIndex = ref<number | null>(null)
const { speedOverlayEnabled, speedLimitOverlayEnabled } = storeToRefs(settingsStore)
const { filterDays: dateRangeDays } = storeToRefs(uiSettingsStore)

let shouldSelectLatest = route.query.selectLatest === '1'
const LOAD_MORE_SIZE = 10

// How many samples the heatmap is drawn from. It renders to a canvas rather than one DOM element
// per point, so it tolerates far more than the polyline overlay, but a 90-day range still has to
// stay quick to rebuild. The spacing between samples follows from this budget and how far the
// trips run in total, so a longer period draws coarser instead of slower.
const MAX_HEATMAP_POINTS = 5000

const mapWrapperRef = ref<HTMLElement | null>(null)
const sidebar = useTemplateRef<InstanceType<typeof TripSidebar>>('sidebar')
const { mapInstance, bindMapReady } = useLeafletMap(mapWrapperRef)

// Vector basemap in the tile pane: follows the theme and the UI language, and everything below
// draws on top of it exactly as it did over the raster tiles.
useBasemap(mapInstance)

// Charging-station / fuel-station / service-area layers: settings bindings, on-demand tile
// fetching/caching, marker clustering, and popups all live in this composable so this view only
// has to wire up the returned bindings and trigger the initial load once the map is ready.
const { speedCamerasAvailable, availableFuelBrands, brandsLoading, poiLoading, loadLayers } =
  usePoiLayers({ mapInstance, vehicleType })

// Static initial centre - controlled by fitAll/flyToStatus after data loads.
const center = DEFAULT_MAP_CENTER

// Trips displayed newest-first in the sidebar; selectedTripIndex is always the real store.trips index.
const newestFirstTrips = computed(() => [...store.trips].reverse())

const {
  displayItems: displayTrips,
  reset: resetTripScroll,
  observe: observeTrips,
} = useInfiniteScroll(newestFirstTrips, LOAD_MORE_SIZE)

function realIndex(newestFirstIdx: number): number {
  return store.trips.length - 1 - newestFirstIdx
}

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

// Whichever speed key is showing opens and folds as one. A phone's map is too short to give the
// key its room by default, so there it starts folded to its button.
const legendExpanded = ref(!isPhoneViewport())

// ── Drawing ────────────────────────────────────────────────────────────────────
const tripLayers = useTripLayers({
  map: mapInstance,
  trips,
  selectedIndex: selectedTripIndex,
  activeIndex: activeTripIndex,
  status,
  activeCarLatLng,
  selectedMatch,
  limitColoursShown,
  heatPoints,
  onSelect: selectTrip,
  takeSelectLatest: () => {
    const take = shouldSelectLatest
    shouldSelectLatest = false
    return take
  },
})

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
        units: units.value,
      }),
    }
  }),
)

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
    tripLayers.drawInitial()
    loadLayers(100)
  })
}

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
  selectedTripIndex.value = selectedTripIndex.value === realIdx ? null : realIdx
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
    observeTrips(sidebar.value?.root ?? null, sidebar.value?.sentinel ?? null)
  })
})
</script>

<template>
  <div class="view-container view-container--map">
    <div class="view-header">
      <h1>{{ t('nav.map') }}</h1>
      <div class="view-header__actions">
        <!-- Filters narrow what the map draws; the layer panel beside it decides what it draws
             at all. -->
        <MapFilterPanel
          :car-takes-fuel="carTakesFuel"
          :car-takes-charge="carTakesCharge"
          :fuel-brands="availableFuelBrands"
          :brands-loading="brandsLoading"
        />
        <MapLayerPanel
          :car-takes-fuel="carTakesFuel"
          :car-takes-charge="carTakesCharge"
          :speed-cameras-available="speedCamerasAvailable"
        />
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
      <TripSidebar
        ref="sidebar"
        :rows="displayRows"
        :loading="store.loading"
        :empty="!store.trips.length"
        :selected-index="selectedTripIndex"
        :active-index="activeTripIndex"
        :skeleton-rows="LOAD_MORE_SIZE"
        @select="selectTrip"
      />

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
                : t('trips.snapped', { distance: units.format('distance', snapStatus.km) })
            }}
          </div>
        </div>

        <SpeedLegend
          v-if="speedOverlayEnabled && selectedTripIndex !== null"
          v-model:expanded="legendExpanded"
        />

        <SpeedLimitLegend
          v-if="speedLimitSummaryOfTrip"
          v-model:expanded="legendExpanded"
          :summary="speedLimitSummaryOfTrip"
          :coloured="limitColoursShown"
        />
      </div>
    </div>
  </div>
</template>

<!-- Global on purpose: Leaflet builds marker and popup HTML itself, outside Vue's renderer,
     so those elements never carry a scope attribute. -->
<style>
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
