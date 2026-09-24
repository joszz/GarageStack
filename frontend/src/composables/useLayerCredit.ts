import { onUnmounted, watch, type Ref } from 'vue'
import type { LeafletMap } from '@/utils/leaflet'

/**
 * Keeps a data credit in the map's attribution control for exactly as long as the layer that needs
 * it is on screen. Leaflet manages credits per layer for layers it created itself; the on-demand
 * POI layers build their own marker clusters, so the credit their source asks for has to be put up
 * and taken down alongside them - a charging-station credit on a map showing no charging stations
 * names a source the map is not using.
 */
export function useLayerCredit(
  mapInstance: Ref<LeafletMap | null>,
  active: Readonly<Ref<boolean>>,
  credit: string,
) {
  // What the credit is currently attached to, so it can be taken off that same map even after the
  // view has moved on to another one.
  let creditedMap: LeafletMap | null = null

  function remove() {
    creditedMap?.attributionControl?.removeAttribution(credit)
    creditedMap = null
  }

  function apply(map: LeafletMap | null, on: boolean) {
    if (creditedMap && (creditedMap !== map || !on)) remove()
    if (!map || !on || creditedMap === map) return
    // Older Leaflet builds and the test doubles can leave the control out; a map without one simply
    // shows no credits, which is not something to throw over.
    if (!map.attributionControl) return
    map.attributionControl.addAttribution(credit)
    creditedMap = map
  }

  watch([mapInstance, active], ([map, on]) => apply(map, on), { immediate: true })

  onUnmounted(remove)
}
