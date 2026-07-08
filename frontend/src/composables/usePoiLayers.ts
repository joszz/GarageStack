import { computed, ref, watch, onUnmounted, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { L, type LeafletMap } from '@/utils/leaflet'
import 'leaflet.markercluster'
import 'leaflet.markercluster/dist/MarkerCluster.css'
import type { VehicleType } from '@/stores/vehicle'
import { useMapSettingsStore } from '@/stores/settingsMap'
import { mapApi } from '@/services/mapApi'
import type { ChargingStation, PoiItem } from '@/services/mapApi'
import { canonicalFuelBrand } from '@/utils/fuelBrands'

type ClusterFactory = {
  markerClusterGroup: (options?: {
    iconCreateFunction?: (cluster: { getChildCount: () => number }) => L.DivIcon
    maxClusterRadius?: number
    animate?: boolean
  }) => L.FeatureGroup
}
const leafWithCluster = L as typeof L & ClusterFactory

function createMarkerCluster(clusterClassName: string): L.FeatureGroup {
  return leafWithCluster.markerClusterGroup({
    maxClusterRadius: 60,
    animate: true,
    iconCreateFunction: (cluster) => {
      const count = cluster.getChildCount()
      return L.divIcon({
        className: '',
        html: `<div class="${clusterClassName}">${count}</div>`,
        iconSize: [36, 36],
        iconAnchor: [18, 18],
      })
    },
  })
}

function createDebouncer() {
  let timer: ReturnType<typeof setTimeout> | null = null
  return {
    trigger(fn: () => void, delayMs: number) {
      if (timer !== null) clearTimeout(timer)
      timer = setTimeout(() => {
        timer = null
        fn()
      }, delayMs)
    },
    cancel() {
      if (timer !== null) clearTimeout(timer)
      timer = null
    },
  }
}

// OSM/Open Charge Map data is crowd-editable - never trust it to be free of markup, so every
// externally-sourced string interpolated into a Leaflet popup (which uses innerHTML) must be escaped.
function escapeHtml(value: string): string {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;')
}

function computeVisibleTileKeys(map: LeafletMap): string[] {
  const bounds = map.getBounds()
  const minCellLat = Math.floor(bounds.getSouth() * 2)
  const maxCellLat = Math.floor(bounds.getNorth() * 2)
  const minCellLng = Math.floor(bounds.getWest() * 2)
  const maxCellLng = Math.floor(bounds.getEast() * 2)
  const keys: string[] = []
  for (let lat = minCellLat; lat <= maxCellLat; lat++) {
    for (let lng = minCellLng; lng <= maxCellLng; lng++) {
      keys.push(`${lat},${lng}`)
    }
  }
  return keys
}

export interface UsePoiLayersOptions {
  mapInstance: Ref<LeafletMap | null>
  vehicleType: ComputedRef<VehicleType | 'unknown'>
  isHev: ComputedRef<boolean>
  isBev: ComputedRef<boolean>
}

/**
 * Owns everything related to the charging-station, fuel-station, and service-area map layers:
 * settings bindings, on-demand tile fetching/caching, marker clustering, and popups. Reacts to
 * pan/zoom (via the map instance passed in) and to the relevant settings toggling on its own.
 */
export function usePoiLayers({ mapInstance, vehicleType, isHev, isBev }: UsePoiLayersOptions) {
  const { t } = useI18n()
  const settingsStore = useMapSettingsStore()

  const chargingStationsEnabled = computed({
    get: () => settingsStore.chargingStationsEnabled,
    set: (v: boolean) => {
      settingsStore.chargingStationsEnabled = v
    },
  })
  const fuelStationsEnabled = computed({
    get: () => settingsStore.fuelStationsEnabled,
    set: (v: boolean) => {
      settingsStore.fuelStationsEnabled = v
    },
  })
  const serviceAreasEnabled = computed({
    get: () => settingsStore.serviceAreasEnabled,
    set: (v: boolean) => {
      settingsStore.serviceAreasEnabled = v
    },
  })
  const fuelBrandFilter = computed({
    get: () => settingsStore.fuelBrandFilter,
    set: (v: string[]) => {
      settingsStore.fuelBrandFilter = v
    },
  })
  const chargingMinPowerKw = computed({
    get: () => settingsStore.chargingMinPowerKw,
    set: (v: number) => {
      settingsStore.chargingMinPowerKw = v
    },
  })
  const chargingMaxPowerKw = computed({
    get: () => settingsStore.chargingMaxPowerKw,
    set: (v: number) => {
      settingsStore.chargingMaxPowerKw = v
    },
  })

  // Slider value: [minKw, maxKw] where max=350 means "no upper limit" (stored as 0 in settings)
  const powerRangeSlider = computed({
    get: (): [number, number] => [
      chargingMinPowerKw.value,
      chargingMaxPowerKw.value === 0 ? 350 : chargingMaxPowerKw.value,
    ],
    set: (value: number[]) => {
      chargingMinPowerKw.value = value[0]!
      chargingMaxPowerKw.value = (value[1] ?? 350) >= 350 ? 0 : value[1]!
    },
  })

  const powerRangeLabel = computed(() => {
    const min = chargingMinPowerKw.value
    const max = chargingMaxPowerKw.value
    if (min === 0 && max === 0) return t('trips.chargingPowerAny')
    const minStr = min === 0 ? t('trips.chargingPowerAny') : `${min} kW`
    const maxStr = max === 0 ? '350+ kW' : `${max} kW`
    return `${minStr} - ${maxStr}`
  })

  function formatPowerTooltip(value: number): string {
    if (value === 0) return t('trips.chargingPowerAny')
    if (value >= 350) return '350+'
    return String(value)
  }

  const cachedFuelBrands = ref<string[]>([])
  const brandsLoading = ref(false)

  let chargingCluster: L.FeatureGroup | null = null
  const chargingDebouncer = createDebouncer()
  let chargingFetchId = 0
  let fuelCluster: L.FeatureGroup | null = null
  let serviceAreaCluster: L.FeatureGroup | null = null
  const fuelDebouncer = createDebouncer()
  const serviceAreaDebouncer = createDebouncer()
  let fuelFetchId = 0
  let serviceAreaFetchId = 0
  const fuelLoadedTiles = new Set<string>()
  const fuelLoadedIds = new Set<string>()
  // All fuel stations fetched so far, keyed by externalId. The brand filter is applied
  // client-side from this cache so checkbox changes never trigger a new API call.
  const fuelAllItems = new Map<string, PoiItem>()
  const serviceAreaLoadedTiles = new Set<string>()
  const serviceAreaLoadedIds = new Set<string>()
  const chargingLoadedTiles = new Set<string>()
  // All stations fetched so far, keyed by station ID. The power filter is applied
  // client-side from this cache so slider changes never trigger a new API call.
  const chargingAllStations = new Map<string, ChargingStation>()
  const poiLoadingCount = ref(0)
  const poiLoading = computed(() => poiLoadingCount.value > 0)

  // Coalesced brand names shown in the filter dropdown (e.g. "BP" covers "BP" and "BP express").
  // See canonicalFuelBrand for the raw-variant -> canonical mapping.
  const availableFuelBrands = computed(() => {
    const brands = new Set<string>(cachedFuelBrands.value.map(canonicalFuelBrand))
    for (const item of fuelAllItems.values()) {
      const tags = item.tags ?? {}
      const brand = tags['brand'] ?? tags['operator'] ?? null
      if (brand) brands.add(canonicalFuelBrand(brand))
    }
    return [...brands].sort((a, b) => a.localeCompare(b))
  })

  async function loadFuelBrands() {
    if (vehicleType.value === 'unknown') return
    brandsLoading.value = true
    try {
      cachedFuelBrands.value = await mapApi.poiBrands('fuel', vehicleType.value)
    } catch {
      // non-fatal
    } finally {
      brandsLoading.value = false
    }
  }

  function getBoundsRadiusKm(): number {
    const map = mapInstance.value
    if (!map) return 10
    const bounds = map.getBounds()
    const distanceM = bounds.getCenter().distanceTo(bounds.getNorthEast())
    return Math.min(Math.ceil(distanceM / 1000), 200)
  }

  function buildPoiPopup(item: PoiItem): string {
    const tags = item.tags ?? {}
    const brand = tags['brand'] ?? tags['operator'] ?? null
    const openingHours = tags['opening_hours'] ?? null
    const title = escapeHtml(item.name ?? item.poiType)
    return `<div class="poi-popup">
    <strong class="poi-popup__title">${title}</strong>
    ${brand ? `<div class="poi-popup__meta">${escapeHtml(brand)}</div>` : ''}
    ${openingHours ? `<div class="poi-popup__meta">${escapeHtml(openingHours)}</div>` : ''}
  </div>`
  }

  function buildChargingPopup(station: ChargingStation): string {
    // Group connectors by type+power, summing quantity so "11 kW × 4" shows instead
    // of four identical rows when OCM returns one record per port rather than one with Quantity=4.
    const grouped = new Map<
      string,
      { type: string | null; powerKw: number | null; count: number }
    >()
    for (const c of station.connectors) {
      if (!c.type && c.powerKw == null) continue
      const key = `${c.type ?? ''}|${c.powerKw ?? ''}`
      const existing = grouped.get(key)
      const qty = c.quantity ?? 1
      if (existing) {
        existing.count += qty
      } else {
        grouped.set(key, { type: c.type, powerKw: c.powerKw, count: qty })
      }
    }

    const connectorItems = [...grouped.values()]
      .map(({ type, powerKw, count }) => {
        const parts = [
          type ? escapeHtml(type) : null,
          powerKw != null ? `${powerKw} kW` : null,
        ].filter(Boolean)
        const suffix = count > 1 ? ` ×${count}` : ''
        return `<li>${parts.join(' · ')}${suffix}</li>`
      })
      .join('')

    const stallLine =
      station.numberOfPoints != null
        ? `<div class="charging-popup__stalls">${station.numberOfPoints} ${station.numberOfPoints === 1 ? t('trips.chargingStall') : t('trips.chargingStalls')}</div>`
        : ''

    const address = [station.addressLine, station.town].filter(Boolean).join(', ')

    return `<div class="charging-popup">
    <strong class="charging-popup__title">${escapeHtml(station.title)}</strong>
    ${station.operator ? `<div class="charging-popup__operator">${escapeHtml(station.operator)}</div>` : ''}
    ${address ? `<div class="charging-popup__address">${escapeHtml(address)}</div>` : ''}
    ${stallLine}
    ${connectorItems ? `<ul class="charging-popup__connectors">${connectorItems}</ul>` : ''}
  </div>`
  }

  function clearChargingMarkers() {
    chargingCluster?.remove()
    chargingCluster = null
    chargingLoadedTiles.clear()
    chargingAllStations.clear()
  }

  function redrawChargingMarkers() {
    const map = mapInstance.value
    if (!map) return
    chargingCluster?.remove()
    chargingCluster = null
    if (!chargingStationsEnabled.value || chargingAllStations.size === 0) return

    chargingCluster = createMarkerCluster('charging-cluster')
    chargingCluster.addTo(map)

    const minKw = chargingMinPowerKw.value
    const maxKw = chargingMaxPowerKw.value

    for (const station of chargingAllStations.values()) {
      if (minKw > 0 && !station.connectors.some((c) => c.powerKw != null && c.powerKw >= minKw))
        continue
      if (maxKw > 0 && !station.connectors.some((c) => c.powerKw == null || c.powerKw <= maxKw))
        continue
      const cls = station.isOperational === false ? ' charging-marker--unknown' : ''
      const icon = L.divIcon({
        className: '',
        html: `<div class="charging-marker${cls}">&#9889;</div>`,
        iconSize: [28, 28],
        iconAnchor: [14, 14],
      })
      const marker = L.marker([station.latitude, station.longitude], { icon })
      marker.bindPopup(buildChargingPopup(station))
      chargingCluster.addLayer(marker)
    }
  }

  function clearPoiMarkers(poiType: 'fuel' | 'service_area') {
    if (poiType === 'fuel') {
      fuelCluster?.remove()
      fuelCluster = null
      fuelLoadedTiles.clear()
      fuelLoadedIds.clear()
      fuelAllItems.clear()
    } else {
      serviceAreaCluster?.remove()
      serviceAreaCluster = null
      serviceAreaLoadedTiles.clear()
      serviceAreaLoadedIds.clear()
    }
  }

  function redrawFuelMarkers() {
    const map = mapInstance.value
    if (!map) return
    fuelCluster?.remove()
    fuelCluster = null
    if (!fuelStationsEnabled.value || fuelAllItems.size === 0) return

    fuelCluster = createMarkerCluster('poi-cluster poi-cluster--fuel')
    fuelCluster.addTo(map)

    const selectedBrands = fuelBrandFilter.value
    for (const item of fuelAllItems.values()) {
      const tags = item.tags ?? {}
      const brand = tags['brand'] ?? tags['operator'] ?? null
      const canonicalBrand = brand ? canonicalFuelBrand(brand) : null
      if (
        selectedBrands.length > 0 &&
        (canonicalBrand === null || !selectedBrands.includes(canonicalBrand))
      )
        continue
      const icon = L.divIcon({
        className: '',
        html: '<div class="poi-marker poi-marker--fuel">&#9981;</div>',
        iconSize: [28, 28],
        iconAnchor: [14, 14],
      })
      const marker = L.marker([item.latitude, item.longitude], { icon })
      marker.bindPopup(buildPoiPopup(item))
      fuelCluster.addLayer(marker)
    }
  }

  async function loadPoiLayer(poiType: 'fuel' | 'service_area', overrideRadius?: number) {
    const map = mapInstance.value
    const enabled = poiType === 'fuel' ? fuelStationsEnabled.value : serviceAreasEnabled.value
    if (!map || !enabled) {
      clearPoiMarkers(poiType)
      return
    }

    // Vehicle type not resolved yet - wait for the vehicleType watch to retry once config loads
    if (poiType === 'fuel' && vehicleType.value === 'unknown') return

    const loadedTiles = poiType === 'fuel' ? fuelLoadedTiles : serviceAreaLoadedTiles
    const loadedIds = poiType === 'fuel' ? fuelLoadedIds : serviceAreaLoadedIds
    const vt = vehicleType.value
    const bounds = map.getBounds()
    const center = bounds.getCenter()
    const centerKey = `${Math.floor(center.lat * 2)},${Math.floor(center.lng * 2)}`

    // Skip only when every visible tile has already been fetched.
    const visibleKeys = overrideRadius ? null : computeVisibleTileKeys(map)
    if (visibleKeys && visibleKeys.every((k) => loadedTiles.has(k))) return

    const radiusKm = overrideRadius ?? getBoundsRadiusKm()
    const fetchId = poiType === 'fuel' ? ++fuelFetchId : ++serviceAreaFetchId

    poiLoadingCount.value++
    try {
      const { items, hasMore } = await mapApi.poi(poiType, center.lat, center.lng, radiusKm, vt)
      const currentId = poiType === 'fuel' ? fuelFetchId : serviceAreaFetchId
      if (fetchId !== currentId || !enabled) return

      let newItems = false

      if (poiType === 'fuel') {
        for (const item of items) {
          if (fuelAllItems.has(item.externalId)) continue
          fuelAllItems.set(item.externalId, item)
          loadedIds.add(item.externalId)
          newItems = true
        }
        if (newItems) redrawFuelMarkers()
      } else {
        let cluster = serviceAreaCluster
        if (!cluster) {
          cluster = createMarkerCluster('poi-cluster poi-cluster--service-area')
          cluster.addTo(map)
          serviceAreaCluster = cluster
        }
        for (const item of items) {
          if (loadedIds.has(item.externalId)) continue
          loadedIds.add(item.externalId)
          newItems = true
          const icon = L.divIcon({
            className: '',
            html: '<div class="poi-marker poi-marker--service-area">&#9654;</div>',
            iconSize: [28, 28],
            iconAnchor: [14, 14],
          })
          const marker = L.marker([item.latitude, item.longitude], { icon })
          marker.bindPopup(buildPoiPopup(item))
          cluster.addLayer(marker)
        }
      }

      loadedTiles.add(centerKey)

      // hasMore = server still has uncached tiles in this radius (either beyond MaxOnDemandTiles,
      // or a fetch was skipped due to Overpass rate-limit backoff).
      // When !hasMore: all tiles are cached -- bulk-mark the viewport and stop.
      // When hasMore: chain another request. Use a longer delay when no new items arrived
      // (server is likely in a rate-limit backoff window) to avoid hammering the API.
      if (visibleKeys) {
        if (!hasMore) {
          for (const k of visibleKeys) loadedTiles.add(k)
        } else if (enabled && visibleKeys.some((k) => !loadedTiles.has(k))) {
          const chainDelay = newItems ? 400 : 5000
          if (poiType === 'fuel') {
            fuelDebouncer.trigger(() => loadPoiLayer(poiType), chainDelay)
          } else {
            serviceAreaDebouncer.trigger(() => loadPoiLayer(poiType), chainDelay)
          }
        }
      }
    } catch {
      // Overpass errors are non-fatal
    } finally {
      poiLoadingCount.value--
    }
  }

  async function loadChargingStations(overrideRadius?: number) {
    const map = mapInstance.value
    if (!map || !chargingStationsEnabled.value || isHev.value) {
      clearChargingMarkers()
      return
    }

    const mc = map.getBounds().getCenter()
    const center = { lat: mc.lat, lng: mc.lng }
    const centerKey = `${Math.floor(center.lat * 2)},${Math.floor(center.lng * 2)}`

    // Skip only when every visible tile has already been fetched.
    const visibleKeys = overrideRadius ? null : computeVisibleTileKeys(map)
    if (visibleKeys && visibleKeys.every((k) => chargingLoadedTiles.has(k))) return

    const fetchId = ++chargingFetchId
    const radiusKm = overrideRadius ?? getBoundsRadiusKm()
    poiLoadingCount.value++
    try {
      // Always fetch unfiltered (0, 0) -- the power filter is applied client-side from
      // chargingAllStations so slider changes never need a new API call.
      const stations = await mapApi.chargingStations(center.lat, center.lng, radiusKm, 0, 0)
      if (fetchId !== chargingFetchId || !chargingStationsEnabled.value) return

      let newStations = false
      for (const station of stations) {
        const id = String(station.id)
        if (!chargingAllStations.has(id)) {
          chargingAllStations.set(id, station)
          newStations = true
        }
      }

      chargingLoadedTiles.add(centerKey)
      if (newStations) redrawChargingMarkers()

      // When no new stations arrived the server has nothing more to cache for this viewport --
      // mark all visible tiles done. When new stations did arrive, chain another pass.
      if (visibleKeys) {
        if (!newStations && chargingAllStations.size > 0) {
          for (const k of visibleKeys) chargingLoadedTiles.add(k)
        } else if (
          chargingStationsEnabled.value &&
          visibleKeys.some((k) => !chargingLoadedTiles.has(k))
        ) {
          chargingDebouncer.trigger(() => loadChargingStations(), 400)
        }
      }
    } catch {
      // OCM is optional; silently ignore errors
    } finally {
      poiLoadingCount.value--
    }
  }

  // Charging stations toggle and filter changes
  watch(chargingStationsEnabled, (enabled) => {
    if (enabled) loadChargingStations()
    else clearChargingMarkers()
  })

  watch(fuelStationsEnabled, (enabled) => {
    if (enabled) {
      loadFuelBrands()
      loadPoiLayer('fuel')
    } else {
      clearPoiMarkers('fuel')
      cachedFuelBrands.value = []
    }
  })

  watch(fuelBrandFilter, () => {
    if (fuelStationsEnabled.value) redrawFuelMarkers()
  })

  watch(serviceAreasEnabled, (enabled) => {
    if (enabled) loadPoiLayer('service_area')
    else clearPoiMarkers('service_area')
  })

  watch(isHev, (hev) => {
    if (hev) clearChargingMarkers()
    else if (chargingStationsEnabled.value) loadChargingStations()
  })

  watch(isBev, (bev) => {
    if (bev) clearPoiMarkers('fuel')
    else if (fuelStationsEnabled.value) loadPoiLayer('fuel')
  })

  // Vehicle type transitions from unknown once fetchConfig resolves - reload fuel layer now we know the type
  watch(vehicleType, (newVt, oldVt) => {
    if (oldVt !== 'unknown' || newVt === 'unknown' || !mapInstance.value) return
    if (fuelStationsEnabled.value) {
      loadFuelBrands()
      loadPoiLayer('fuel')
    }
  })

  watch(chargingMinPowerKw, () => {
    if (chargingStationsEnabled.value) redrawChargingMarkers()
  })
  watch(chargingMaxPowerKw, () => {
    if (chargingStationsEnabled.value) redrawChargingMarkers()
  })

  // Reload on-demand layers as the viewport moves, once a map exists.
  watch(mapInstance, (map, _prev, onCleanup) => {
    if (!map) return
    const handler = () => {
      if (chargingStationsEnabled.value) {
        chargingDebouncer.trigger(() => loadChargingStations(), 500)
      }
      if (fuelStationsEnabled.value) {
        fuelDebouncer.trigger(() => loadPoiLayer('fuel'), 500)
      }
      if (serviceAreasEnabled.value) {
        serviceAreaDebouncer.trigger(() => loadPoiLayer('service_area'), 500)
      }
    }
    map.on('moveend zoomend', handler)
    onCleanup(() => map.off('moveend zoomend', handler))
  })

  onUnmounted(() => {
    chargingDebouncer.cancel()
    fuelDebouncer.cancel()
    serviceAreaDebouncer.cancel()
    clearChargingMarkers()
    clearPoiMarkers('fuel')
    clearPoiMarkers('service_area')
  })

  return {
    // settings-backed bindings for the filter panel
    chargingStationsEnabled,
    fuelStationsEnabled,
    serviceAreasEnabled,
    fuelBrandFilter,
    chargingMinPowerKw,
    chargingMaxPowerKw,
    powerRangeSlider,
    powerRangeLabel,
    formatPowerTooltip,
    availableFuelBrands,
    brandsLoading,
    poiLoading,
    // actions the view triggers directly (initial load on map-ready, brand refresh on mount)
    loadFuelBrands,
    loadChargingStations,
    loadPoiLayer,
  }
}
