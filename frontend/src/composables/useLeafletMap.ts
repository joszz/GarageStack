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
  let generation = 0

  function unbindMap() {
    generation += 1
    resizeObserver?.disconnect()
    resizeObserver = null
    mapInstance.value = null
  }

  function bindMapReady(map: LeafletMap, afterReady?: (map: LeafletMap) => void) {
    unbindMap()
    mapInstance.value = map
    const current = generation

    if (wrapperRef?.value) {
      resizeObserver = new ResizeObserver(() => {
        if (current === generation) map.invalidateSize()
      })
      resizeObserver.observe(wrapperRef.value)
    }

    nextTick(() => {
      requestAnimationFrame(() => {
        if (current !== generation) return
        map.invalidateSize()
        afterReady?.(map)
      })
    })
  }

  onUnmounted(unbindMap)

  return { mapInstance, bindMapReady, unbindMap }
}
