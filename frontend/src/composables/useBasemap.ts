import { onUnmounted, watch, type Ref } from 'vue'
import { storeToRefs } from 'pinia'
import type { StyleSpecification } from 'maplibre-gl'
import type { maplibreGL } from '@maplibre/maplibre-gl-leaflet'
import { L, type LeafletMap } from '@/utils/leaflet'
import { useUiSettingsStore } from '@/stores/settingsUi'
import type { Locale } from '@/stores/settingsShared'
import {
  BASEMAP_STYLE_URLS,
  RASTER_FALLBACK_MAX_ZOOM,
  RASTER_FALLBACK_TILE_URL,
  boostLabelContrast,
  localizeStyleLabels,
  supportsWebGl,
  type MapStyle,
} from '@/utils/basemapStyle'
import { OSM_ATTRIBUTION, VECTOR_ATTRIBUTION } from '@/utils/credits'

type MaplibreLayer = ReturnType<typeof maplibreGL>

// One in-flight request per style URL, shared by every map on the page and kept for the session:
// the dashboard preview and the full map ask for the same style, and switching theme back and
// forth should not refetch it. The response text is parsed per use, so no two maps ever hand
// MapLibre the same style object.
const styleTextCache = new Map<string, Promise<string>>()

/** The two style rewrites every map applies: readable labels, in the interface language. */
function prepareStyle(style: MapStyle, locale: Locale): StyleSpecification {
  return localizeStyleLabels(boostLabelContrast(style), locale) as unknown as StyleSpecification
}

async function loadStyle(url: string): Promise<MapStyle> {
  let pending = styleTextCache.get(url)
  if (!pending) {
    pending = fetch(url).then((response) => {
      if (!response.ok) throw new Error(`Basemap style ${url} responded ${response.status}`)
      return response.text()
    })
    // A rejected promise must not stay cached, or one offline moment would pin this session to
    // the raster fallback for good.
    pending.catch(() => styleTextCache.delete(url))
    styleTextCache.set(url, pending)
  }
  return JSON.parse(await pending) as MapStyle
}

/**
 * Puts a vector basemap under a Leaflet map. MapLibre GL draws OpenStreetMap vector tiles into
 * Leaflet's tile pane, so the basemap follows the app's dark/light theme and its labels follow
 * the UI language, while everything above it (markers, clusters, routes, the heatmap) stays
 * ordinary Leaflet. Both libraries are loaded lazily, so a page without a map never pays for the
 * renderer, and a browser without WebGL falls back to the raster tiles this replaced.
 */
export function useBasemap(mapInstance: Ref<LeafletMap | null>) {
  const { theme, locale } = storeToRefs(useUiSettingsStore())

  let vectorActive = false
  let layer: L.Layer | null = null
  let glLayer: MaplibreLayer | null = null
  // Bumped whenever the target map or its style changes, so an await that resolves after the
  // world moved on (theme switched twice, view unmounted mid-fetch) knows to drop its result.
  let generation = 0

  function detach() {
    generation += 1
    layer?.remove()
    layer = null
    glLayer = null
    vectorActive = false
  }

  function addRasterFallback(map: LeafletMap) {
    layer = L.tileLayer(RASTER_FALLBACK_TILE_URL, {
      attribution: OSM_ATTRIBUTION,
      maxZoom: RASTER_FALLBACK_MAX_ZOOM,
    }).addTo(map)
  }

  async function addVectorBasemap(map: LeafletMap, token: number) {
    try {
      const [style, maplibre] = await Promise.all([
        loadStyle(BASEMAP_STYLE_URLS[theme.value]),
        import('@/utils/maplibreLayer'),
      ])
      if (token !== generation) return

      // The credit has to be stated here. Left to itself the plugin copies whatever the style's
      // sources declare, and OpenFreeMap's styles declare nothing, so the map carried no credit
      // at all: not the basemap's, and not OpenStreetMap's underneath it. A deployment serving
      // its own tiles from a style that does declare a credit still shows this one, which names
      // the data rather than the host and so stays true either way.
      glLayer = maplibre.maplibreGL({
        attributionControl: { customAttribution: VECTOR_ATTRIBUTION },
        style: prepareStyle(style, locale.value),
        // Leaflet owns panning and zooming and jumps the GL map after each change. Fading labels
        // in from scratch on every one of those jumps reads as flicker while panning.
        fadeDuration: 0,
        // Leaflet takes its zoom limits from the layers on the map. Vector tiles overzoom
        // cleanly, but past this the data has nothing left to add.
        maxZoom: 20,
      })
      glLayer.addTo(map)
      layer = glLayer
      vectorActive = true
    } catch (error) {
      console.warn('[map] vector basemap unavailable, falling back to raster tiles', error)
      if (token === generation) addRasterFallback(map)
    }
  }

  async function applyStyle(token: number) {
    const gl = glLayer?.getMaplibreMap()
    if (!gl) return
    try {
      const style = await loadStyle(BASEMAP_STYLE_URLS[theme.value])
      if (token !== generation || glLayer?.getMaplibreMap() !== gl) return
      // Diffing (setStyle's default) keeps the tiles already on screen when only colours change,
      // so a theme switch recolours the map in place instead of blanking it.
      gl.setStyle(prepareStyle(style, locale.value))
    } catch (error) {
      // Keep whatever is on screen: a failed restyle is a wrong-coloured map, not a broken one.
      console.warn('[map] could not restyle the basemap', error)
    }
  }

  watch(
    mapInstance,
    (map) => {
      detach()
      if (!map) return
      // The vector tiles credit three projects, which together with Leaflet's own "Leaflet"
      // prefix wraps onto a second line and swallows a quarter of the dashboard's map card.
      // The data credits have to stay; Leaflet's does not (BSD, no attribution clause).
      if (map.attributionControl) map.attributionControl.setPrefix('')
      if (!supportsWebGl()) {
        addRasterFallback(map)
        return
      }
      void addVectorBasemap(map, generation)
    },
    { immediate: true },
  )

  // A theme switch recolours the existing map; a language switch relabels it. Both are the same
  // operation - hand MapLibre a freshly localized style for the current theme.
  watch([theme, locale], () => {
    if (!vectorActive) return
    generation += 1
    void applyStyle(generation)
  })

  onUnmounted(detach)
}
