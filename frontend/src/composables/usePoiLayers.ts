import { computed, ref, watch, onUnmounted, type ComputedRef, type Ref } from 'vue'
import { storeToRefs } from 'pinia'
import { useI18n } from 'vue-i18n'
import type { LeafletMap } from '@/utils/leaflet'
import { createMarker, createTileLayer, escapeHtml } from './poiTileLayer'
import type { VehicleType } from '@/stores/vehicle'
import { useMapSettingsStore } from '@/stores/settingsMap'
import { mapApi } from '@/services/mapApi'
import type { ChargingStation, PoiItem } from '@/services/mapApi'
import { canonicalFuelBrand } from '@/utils/fuelBrands'

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

function poiBrand(item: PoiItem): string | null {
  const tags = item.tags ?? {}
  const brand = tags['brand'] ?? tags['operator'] ?? null
  return brand ? canonicalFuelBrand(brand) : null
}

export interface UsePoiLayersOptions {
  mapInstance: Ref<LeafletMap | null>
  vehicleType: ComputedRef<VehicleType>
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
  // These are plain refs on the store already (Composition-API-style defineStore), so
  // storeToRefs gives directly writable, reactive bindings with no wrapper needed - a
  // computed({get, set}) proxy per field would only reproduce what storeToRefs already does.
  const {
    chargingStationsEnabled,
    fuelStationsEnabled,
    serviceAreasEnabled,
    fuelBrandFilter,
    chargingMinPowerKw,
    chargingMaxPowerKw,
  } = storeToRefs(settingsStore)

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
  const poiLoadingCount = ref(0)
  const poiLoading = computed(() => poiLoadingCount.value > 0)
  const trackLoading = (delta: number) => {
    poiLoadingCount.value += delta
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

  // An HEV has no plug and a BEV has no tank, so those layers are not merely off, they do not
  // apply. Folding that into "enabled" keeps one reason-to-be-visible per layer.
  const chargingLayerEnabled = computed(() => chargingStationsEnabled.value && !isHev.value)
  const fuelLayerEnabled = computed(() => fuelStationsEnabled.value && !isBev.value)

  const chargingLayer = createTileLayer<ChargingStation>({
    map: mapInstance,
    enabled: chargingLayerEnabled,
    clusterClassName: 'charging-cluster',
    idOf: (station) => String(station.id),
    // Always fetch unfiltered - the power filter is applied client-side from the cache, so
    // moving the slider never needs a new API call.
    fetchItems: async (center, radiusKm) => ({
      items: await mapApi.chargingStations(center.lat, center.lng, radiusKm, 0, 0),
      hasMore: false,
    }),
    include: (station) => {
      const minKw = chargingMinPowerKw.value
      const maxKw = chargingMaxPowerKw.value
      if (minKw > 0 && !station.connectors.some((c) => c.powerKw != null && c.powerKw >= minKw))
        return false
      if (maxKw > 0 && !station.connectors.some((c) => c.powerKw == null || c.powerKw <= maxKw))
        return false
      return true
    },
    toMarker: (station) =>
      createMarker(
        station.latitude,
        station.longitude,
        `charging-marker${station.isOperational === false ? ' charging-marker--unknown' : ''}`,
        '&#9889;',
        buildChargingPopup(station),
      ),
    // OCM answers in full per request, so "nothing new arrived" is what says the viewport is
    // covered. An empty first answer still chains, since it may be a gap rather than the end.
    moreToFetch: ({ newItems, cached }) => newItems || cached === 0,
    chainDelayMs: () => 400,
    onLoadingChange: trackLoading,
  })

  const fuelLayer = createTileLayer<PoiItem>({
    map: mapInstance,
    enabled: fuelLayerEnabled,
    // The server needs a vehicle type to decide which fuels matter; it arrives with the config
    // fetch, and the vehicleType watch below reloads once it does.
    ready: () => vehicleType.value !== 'unknown',
    clusterClassName: 'poi-cluster poi-cluster--fuel',
    idOf: (item) => item.externalId,
    fetchItems: (center, radiusKm) =>
      mapApi.poi('fuel', center.lat, center.lng, radiusKm, vehicleType.value),
    include: (item) => {
      const selected = fuelBrandFilter.value
      if (selected.length === 0) return true
      const brand = poiBrand(item)
      return brand !== null && selected.includes(brand)
    },
    toMarker: (item) =>
      createMarker(
        item.latitude,
        item.longitude,
        'poi-marker poi-marker--fuel',
        '&#9981;',
        buildPoiPopup(item),
      ),
    // Overpass is paged and rate-limited: hasMore is the server saying it holds uncached tiles.
    // A pass that added nothing is likely a backoff window, so back off with it.
    moreToFetch: ({ hasMore }) => hasMore,
    chainDelayMs: ({ newItems }) => (newItems ? 400 : 5000),
    onLoadingChange: trackLoading,
  })

  const serviceAreaLayer = createTileLayer<PoiItem>({
    map: mapInstance,
    enabled: serviceAreasEnabled,
    clusterClassName: 'poi-cluster poi-cluster--service-area',
    idOf: (item) => item.externalId,
    fetchItems: (center, radiusKm) =>
      mapApi.poi('service_area', center.lat, center.lng, radiusKm, vehicleType.value),
    toMarker: (item) =>
      createMarker(
        item.latitude,
        item.longitude,
        'poi-marker poi-marker--service-area',
        '&#9654;',
        buildPoiPopup(item),
      ),
    moreToFetch: ({ hasMore }) => hasMore,
    chainDelayMs: ({ newItems }) => (newItems ? 400 : 5000),
    onLoadingChange: trackLoading,
  })

  const layers = [chargingLayer, fuelLayer, serviceAreaLayer]

  // Coalesced brand names shown in the filter dropdown (e.g. "BP" covers "BP" and "BP express").
  // See canonicalFuelBrand for the raw-variant -> canonical mapping.
  const availableFuelBrands = computed(() => {
    const brands = new Set<string>(cachedFuelBrands.value.map(canonicalFuelBrand))
    for (const item of fuelLayer.items.values()) {
      const brand = poiBrand(item)
      if (brand) brands.add(brand)
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

  /** Loads every enabled layer; each one skips itself when it is off or already covered. */
  function loadLayers(overrideRadiusKm?: number) {
    for (const layer of layers) layer.load(overrideRadiusKm)
  }

  watch(fuelLayerEnabled, (on) => {
    if (on) loadFuelBrands()
    else cachedFuelBrands.value = []
  })

  watch(fuelBrandFilter, () => fuelLayer.redraw())
  watch([chargingMinPowerKw, chargingMaxPowerKw], () => chargingLayer.redraw())

  // Vehicle type transitions from unknown once fetchConfig resolves - load the layers that
  // were waiting for it now that the type is known.
  watch(vehicleType, (newType, oldType) => {
    if (oldType !== 'unknown' || newType === 'unknown' || !mapInstance.value) return
    loadFuelBrands()
    fuelLayer.load()
  })

  // Reload on-demand layers as the viewport moves, once a map exists.
  watch(mapInstance, (map, _prev, onCleanup) => {
    if (!map) return
    const handler = () => {
      for (const layer of layers) layer.scheduleLoad(500)
    }
    map.on('moveend zoomend', handler)
    onCleanup(() => map.off('moveend zoomend', handler))
  })

  onUnmounted(() => {
    for (const layer of layers) layer.dispose()
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
    loadLayers,
  }
}
