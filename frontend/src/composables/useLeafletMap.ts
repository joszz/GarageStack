import { nextTick, onUnmounted, shallowRef, type Ref } from 'vue'
import type { LeafletMap } from '@/utils/leaflet'

/**
 * Shared @ready wiring for Leaflet map views: stores the map instance, keeps it in sync with
 * its wrapper's size via ResizeObserver (when a wrapper ref is given), and defers afterReady
 * until the browser has actually laid out the container. Without the requestAnimationFrame,
 * clientHeight is still 0 on mobile right after nextTick because the flex/grid layout pass
 * hasn't run yet.
 */
export function useLeafletMap(wrapperRef?: Ref<HTMLElement | null>) {
  const mapInstance = shallowRef<LeafletMap | null>(null)
  let resizeObserver: ResizeObserver | null = null

  function bindMapReady(map: LeafletMap, afterReady?: (map: LeafletMap) => void) {
    mapInstance.value = map

    if (wrapperRef?.value) {
      resizeObserver = new ResizeObserver(() => map.invalidateSize())
      resizeObserver.observe(wrapperRef.value)
    }

    nextTick(() => {
      requestAnimationFrame(() => {
        map.invalidateSize()
        afterReady?.(map)
      })
    })
  }

  onUnmounted(() => resizeObserver?.disconnect())

  return { mapInstance, bindMapReady }
}
