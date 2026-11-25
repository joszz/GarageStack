<script setup lang="ts">
import '@/assets/dashboard.css'
import { onUnmounted, shallowRef, useTemplateRef, watch } from 'vue'
import {
  L,
  boundsAroundCar,
  fitToBounds,
  insetWithin,
  setViewInBox,
  type LeafletMap,
  type MapInset,
} from '@/utils/leaflet'
import { useBasemap, type BasemapKind } from '@/composables/useBasemap'
import { useCarLocation } from '@/composables/useCarLocation'
import { buildCarMarkerIcon, moveCarMarker } from '@/utils/mapCarIcon'
import { latLngsOf } from '@/utils/geo'

/**
 * The car's map behind the whole dashboard. The car stands in the middle of `spot`, the room the
 * location card leaves free, and the cards lie over the rest of the map. It only shows where the
 * car is, so it cannot be panned or zoomed; the full map is a click away.
 */
const props = defineProps<{ spot: HTMLElement | null }>()
// Which basemap is drawn, so the spot's bar can credit it (the map's own corners are covered).
const emit = defineEmits<{ basemap: [kind: BasemapKind | null] }>()

const { status, location, hasLocation, center, activeTrip, openFullMap } = useCarLocation()

// As close as the location card's map, a step closer: there is more room around the car here.
const PARKED_ZOOM = 15
// Pixels kept clear inside the spot around a route, so its ends do not touch the cards.
const ROUTE_PADDING = 24

const container = useTemplateRef<HTMLElement>('container')
const mapInstance = shallowRef<LeafletMap | null>(null)
// As on the location card: the dashboard is the page most visits start on, so its cards come
// first and the basemap fills in once the page has loaded.
const { basemap } = useBasemap(mapInstance, { deferred: true })
watch(basemap, (kind) => emit('basemap', kind), { immediate: true })

let marker: L.Marker | null = null
let routeLine: L.Polyline | null = null
let observer: ResizeObserver | null = null

/** Where the spot lies in the map, or null while either has no size yet. */
function spotInset(map: LeafletMap): MapInset | null {
  const spot = props.spot
  if (!spot) return null
  const outer = map.getContainer().getBoundingClientRect()
  const inner = spot.getBoundingClientRect()
  if (outer.width === 0 || inner.width === 0 || inner.height === 0) return null
  return insetWithin(outer, inner)
}

function frame() {
  const map = mapInstance.value
  if (!map || !hasLocation.value) return
  const inset = spotInset(map)
  if (!inset) return

  const points = activeTrip.value?.points ?? []
  const bounds = points.length ? boundsAroundCar(center.value, points) : null
  if (bounds && !bounds.getNorthEast().equals(bounds.getSouthWest())) {
    fitToBounds(map, bounds, {
      topLeft: [inset.left + ROUTE_PADDING, inset.top + ROUTE_PADDING],
      bottomRight: [inset.right + ROUTE_PADDING, inset.bottom + ROUTE_PADDING],
    })
  } else {
    setViewInBox(map, center.value, PARKED_ZOOM, inset)
  }
}

// Parked: the plain pin, as on the location card, which opens the full map. Driving: the route so
// far and the car turned to its heading. Out of the tab order: the spot's own button opens the
// full map from the keyboard.
function drawCar() {
  const map = mapInstance.value
  marker?.remove()
  marker = null
  routeLine?.remove()
  routeLine = null
  if (!map || !hasLocation.value) return

  const trip = activeTrip.value
  if (trip && trip.points.length >= 2) {
    routeLine = L.polyline(latLngsOf(trip.points), {
      color: '#3b82f6',
      weight: 4,
      opacity: 0.8,
      interactive: false,
    }).addTo(map)
  }
  marker = trip
    ? L.marker(center.value, {
        icon: buildCarMarkerIcon(status.value?.heading ?? 0),
        keyboard: false,
      })
    : L.marker(center.value, { keyboard: false })
  marker.on('click', openFullMap).addTo(map)
}

function destroyMap() {
  observer?.disconnect()
  observer = null
  marker = null
  routeLine = null
  mapInstance.value?.remove()
  mapInstance.value = null
}

// The map exists while the car has a position: the container is only rendered then.
watch(
  container,
  (element) => {
    destroyMap()
    if (!element) return
    const map = L.map(element, {
      zoomControl: false,
      // Nearly all of the map lies under the cards, its corners too, so the credit it must carry
      // is said in the spot's bar instead (DashboardParkingSpot), where it is always in view.
      attributionControl: false,
      dragging: false,
      scrollWheelZoom: false,
      doubleClickZoom: false,
      touchZoom: false,
      boxZoom: false,
      keyboard: false,
    })
    mapInstance.value = map
    // The dashboard grows as its cards load and the spot moves with the layout, so either changing
    // size puts the car back in the middle of the spot.
    observer = new ResizeObserver(() => {
      map.invalidateSize({ pan: false })
      frame()
    })
    observer.observe(element)
    if (props.spot) observer.observe(props.spot)
    drawCar()
    frame()
  },
  { flush: 'post' },
)

// Edit mode swaps the spot for a placeholder of its own, and a layout change can replace it.
watch(
  () => props.spot,
  (spot, previous) => {
    if (previous) observer?.unobserve(previous)
    if (spot) observer?.observe(spot)
    frame()
  },
)

// A new report moves the car (and on a trip, turns it) and may grow the route.
watch(status, (s) => {
  const point = location.value
  if (!mapInstance.value || !point) return
  if (marker && activeTrip.value) {
    moveCarMarker(marker, [point.latitude, point.longitude], s?.heading ?? null)
  } else {
    marker?.setLatLng([point.latitude, point.longitude])
  }
  frame()
})

// A trip starting or ending swaps the pin for the car, or back, and the framing with it.
watch(activeTrip, () => {
  drawCar()
  frame()
})

onUnmounted(destroyMap)
</script>

<template>
  <div class="map-backdrop">
    <div v-if="hasLocation" ref="container" class="map-backdrop__map" />
    <div class="map-backdrop__fade" />
  </div>
</template>
