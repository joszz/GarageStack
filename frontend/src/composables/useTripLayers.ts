import { computed, nextTick, onUnmounted, watch, type Ref } from 'vue'
import { storeToRefs } from 'pinia'
import type { TripMatch } from '@/composables/useTripMatch'
import type { TelemetrySnapshot, Trip } from '@/services/vehicleApi'
import { useMapSettingsStore } from '@/stores/settingsMap'
import { heatLayer as createHeatLayer } from '@/utils/heatLayer'
import { fitToPoints, L, type LeafletMap } from '@/utils/leaflet'
import { buildCarMarkerIcon } from '@/utils/mapCarIcon'
import { speedLimitSegments } from '@/utils/speedLimits'
import { speedSegments, speedToColor, tripColor } from '@/utils/speedColors'

type HeatPoints = Parameters<typeof createHeatLayer>[0]

export interface TripLayersOptions {
  map: Ref<LeafletMap | null>
  trips: Ref<Trip[]>
  /** The trip drawn on its own, as an index into `trips`; null draws them all. */
  selectedIndex: Ref<number | null>
  /** The trip being driven right now, whose end is the car rather than a flag. */
  activeIndex: Ref<number | null>
  status: Ref<TelemetrySnapshot | null>
  /** Where the car is while driving: the live position, else the active trip's last fix. */
  activeCarLatLng: Ref<[number, number] | null>
  /** The selected trip's snapped line, once the matcher has it. */
  selectedMatch: Ref<TripMatch | null>
  /** Whether the selected trip is drawn in speed limit colours. */
  limitColoursShown: Ref<boolean>
  heatPoints: Ref<HeatPoints>
  /** A trip's line was clicked. */
  onSelect: (index: number) => void
  /** Whether the view was opened to show the newest trip; true once, when trips first arrive. */
  takeSelectLatest: () => boolean
}

/**
 * Draws trips on the map with Leaflet: every trip as a line with the heatmap under them, or one
 * selected trip on its own, coloured by speed or by speed limit when asked, with its start and
 * finish. Frames the map on what it shows and keeps the live car marker moving. Leaflet is
 * imperative, so this redraws on every change that affects the picture.
 */
export function useTripLayers(o: TripLayersOptions) {
  const { heatmapEnabled, routeOutlineEnabled, speedOverlayEnabled, speedLimitOverlayEnabled } =
    storeToRefs(useMapSettingsStore())

  let heatLayer: L.Layer | null = null
  let routeLines: L.Polyline[] = []
  let startMarker: L.Marker | null = null
  let endMarker: L.Marker | null = null
  let mapUpdateRaf: number | null = null
  let hasCenteredOnStatus = false
  // Where the redraw after a deselection centres instead of framing every trip, when the car
  // was asked for.
  let carToShow: [number, number] | null = null

  const allPoints = computed<[number, number][]>(() =>
    o.trips.value.flatMap((trip) =>
      trip.points.map((p) => [p.latitude, p.longitude] as [number, number]),
    ),
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
    const map = o.map.value
    if (!map || o.heatPoints.value.length === 0 || !heatmapEnabled.value) return
    const layer = createHeatLayer(o.heatPoints.value, {
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

  function removeHeatLayer() {
    if (heatLayer) {
      heatLayer.remove()
      heatLayer = null
    }
  }

  function buildRouteLines() {
    const map = o.map.value
    if (!map) return
    clearRouteLines()
    o.trips.value.forEach((trip, i) => {
      const pts = trip.points.map((p) => [p.latitude, p.longitude] as [number, number])
      if (pts.length < 2) return
      if (routeOutlineEnabled.value) {
        const border = L.polyline(pts, { color: '#111', weight: 7, opacity: 0.4 })
        border.on('click', () => o.onSelect(i))
        border.addTo(map)
        routeLines.push(border)
      }
      const line = L.polyline(pts, { color: tripColor(i), weight: 3, opacity: 0.75 })
      line.on('click', () => o.onSelect(i))
      line.addTo(map)
      routeLines.push(line)
    })
  }

  function buildSelectedLine() {
    const map = o.map.value
    const idx = o.selectedIndex.value
    if (!map || idx === null) return
    clearRouteLines()
    const trip = o.trips.value[idx]
    if (!trip) return
    const pts = trip.points
    if (pts.length < 2) return

    // The snapped line when there is one, the raw fixes until then: the map never waits on the
    // matcher, it redraws once the answer lands.
    const match = o.selectedMatch.value
    const coords =
      match?.coordinates ?? pts.map((p) => [p.latitude, p.longitude] as [number, number])

    if (routeOutlineEnabled.value) {
      const border = L.polyline(coords, { color: '#111', weight: 9, opacity: 0.4 })
      border.addTo(map)
      routeLines.push(border)
    }

    if (o.limitColoursShown.value && match) {
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
    const map = o.map.value
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

    if (realIdx === o.activeIndex.value && o.activeCarLatLng.value) {
      endMarker = L.marker(o.activeCarLatLng.value, {
        icon: buildCarMarkerIcon(o.status.value?.heading ?? 0),
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

  function fitPoints(pts: [number, number][]) {
    if (o.map.value) fitToPoints(o.map.value, pts, 32)
  }

  function fitAll() {
    const s = o.status.value
    if (allPoints.value.length > 0) {
      fitPoints(allPoints.value)
    } else if (s?.latitude != null && s?.longitude != null) {
      o.map.value?.setView([s.latitude, s.longitude], 14, { animate: false })
    }
  }

  function fitTrip(trip: Trip) {
    fitPoints(trip.points.map((p) => [p.latitude, p.longitude] as [number, number]))
  }

  /**
   * The first picture once the map exists: every trip, framed, or the car when there are none
   * yet. Later arrivals of trips redraw on their own.
   */
  function drawInitial() {
    const map = o.map.value
    const s = o.status.value
    if (!map) return
    if (allPoints.value.length > 0) {
      buildHeatLayer()
      buildRouteLines()
      fitAll()
    } else if (s?.latitude != null && s?.longitude != null) {
      hasCenteredOnStatus = true
      map.setView([s.latitude, s.longitude], 14, { animate: false })
    }
  }

  /**
   * Centres on the car at street level, dropping any selected trip. The redraw that dropping the
   * selection sets off would otherwise frame every trip, a frame after the car was shown.
   */
  function showCar(latLng: [number, number]) {
    if (o.selectedIndex.value === null) {
      o.map.value?.setView(latLng, 14, { animate: false })
      return
    }
    carToShow = latLng
    o.selectedIndex.value = null
  }

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

  // Center on car position only once on initial load. Subsequent status updates (SignalR
  // reconnects every ~60s) must not move the map away from where the user is looking.
  watch(o.status, (s) => {
    if (!o.map.value || s?.latitude == null || s?.longitude == null) return
    if (!hasCenteredOnStatus && allPoints.value.length === 0) {
      hasCenteredOnStatus = true
      o.map.value.setView([s.latitude, s.longitude], 14, { animate: false })
    }
  })

  // Keep the active trip's car marker following the live position/heading while it's the
  // selected trip, without a full rebuild (would reset the speed-overlay/route-outline state).
  watch(o.status, (s) => {
    if (!endMarker || o.selectedIndex.value === null) return
    if (o.selectedIndex.value !== o.activeIndex.value) return
    if (s?.latitude == null || s?.longitude == null) return
    endMarker.setLatLng([s.latitude, s.longitude])
    endMarker.setIcon(buildCarMarkerIcon(s.heading ?? 0))
  })

  // When trips load after the map is ready, rebuild layers and fit
  watch(allPoints, async (pts) => {
    if (pts.length === 0 || !o.map.value) return
    await nextTick()
    buildHeatLayer()
    buildRouteLines()
    if (o.takeSelectLatest()) o.onSelect(o.trips.value.length - 1)
    else fitAll()
  })

  // Trip selection drives map display and popover position.
  watch(o.selectedIndex, (idx) => {
    scheduleMapUpdate(() => {
      if (idx === null) {
        if (heatmapEnabled.value) buildHeatLayer()
        buildRouteLines()
        if (carToShow) o.map.value?.setView(carToShow, 14, { animate: false })
        else fitAll()
        carToShow = null
      } else {
        removeHeatLayer()
        buildSelectedLine()
        const trip = o.trips.value[idx]
        if (trip) fitTrip(trip)
      }
    })
  })

  // Heatmap toggle while no trip is selected
  watch(heatmapEnabled, (enabled) => {
    if (o.selectedIndex.value !== null) return
    if (enabled) buildHeatLayer()
    else removeHeatLayer()
  })

  // Speed overlay toggle while a trip is selected. Only one of the two overlays can colour the same
  // line, so switching one on switches the other off rather than letting one quietly win.
  watch(speedOverlayEnabled, (on) => {
    if (on) speedLimitOverlayEnabled.value = false
    if (o.selectedIndex.value === null) return
    scheduleMapUpdate(buildSelectedLine)
  })

  watch(speedLimitOverlayEnabled, (on) => {
    if (on) speedOverlayEnabled.value = false
    if (o.selectedIndex.value === null) return
    scheduleMapUpdate(buildSelectedLine)
  })

  // Route outline toggle: rebuild whichever layer is currently active
  watch(routeOutlineEnabled, () => {
    scheduleMapUpdate(() => {
      if (o.selectedIndex.value === null) buildRouteLines()
      else buildSelectedLine()
    })
  })

  // The snapped line lands a moment after the trip was drawn from its raw fixes, and disappears
  // again when snapping is switched off: either way the selected trip is redrawn in place.
  watch(o.selectedMatch, () => {
    if (o.selectedIndex.value === null) return
    scheduleMapUpdate(buildSelectedLine)
  })

  onUnmounted(() => {
    if (mapUpdateRaf !== null) cancelAnimationFrame(mapUpdateRaf)
    removeHeatLayer()
  })

  return { drawInitial, showCar }
}
