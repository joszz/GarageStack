import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, nextTick, ref } from 'vue'
import { enableAutoUnmount, mount } from '@vue/test-utils'
import { L, type LeafletMap } from '@/utils/leaflet'
import { useLeafletMap } from '../useLeafletMap'

enableAutoUnmount(afterEach)

const observers: TestResizeObserver[] = []
const frames: FrameRequestCallback[] = []
const maps: LeafletMap[] = []

class TestResizeObserver implements ResizeObserver {
  observe = vi.fn<(target: Element, options?: ResizeObserverOptions) => void>()
  unobserve = vi.fn<(target: Element) => void>()
  disconnect = vi.fn<() => void>()

  constructor(readonly callback: ResizeObserverCallback) {
    observers.push(this)
  }
}

function fakeMap(): LeafletMap {
  const map = L.map(document.createElement('div'), {
    zoomControl: false,
    attributionControl: false,
  })
  vi.spyOn(map, 'invalidateSize')
  maps.push(map)
  return map
}

function mountMap() {
  let map!: ReturnType<typeof useLeafletMap>
  const target = document.createElement('div')
  const wrapper = mount(
    defineComponent({
      setup() {
        map = useLeafletMap(ref(target))
        return () => h('div')
      },
    }),
  )
  return { map, target, wrapper }
}

describe('useLeafletMap', () => {
  beforeEach(() => {
    observers.length = 0
    frames.length = 0
    vi.stubGlobal('ResizeObserver', TestResizeObserver)
    vi.stubGlobal(
      'requestAnimationFrame',
      vi.fn<(callback: FrameRequestCallback) => number>((callback) => frames.push(callback)),
    )
  })

  afterEach(() => {
    for (const map of maps.splice(0)) map.remove()
    vi.unstubAllGlobals()
  })

  it('disconnects and ignores queued resize and layout callbacks after unbinding', async () => {
    const { map, target } = mountMap()
    const old = fakeMap()
    const afterReady = vi.fn<(ready: LeafletMap) => void>()
    map.bindMapReady(old, afterReady)
    await nextTick()
    const observer = observers[0]!
    expect(observer.observe).toHaveBeenCalledWith(target)

    map.unbindMap()
    observer.callback([], observer)
    frames[0]!(0)

    expect(observer.disconnect).toHaveBeenCalledOnce()
    expect(map.mapInstance.value).toBeNull()
    expect(old.invalidateSize).not.toHaveBeenCalled()
    expect(afterReady).not.toHaveBeenCalled()
  })

  it('replaces an old map and only lays out the current one', async () => {
    const { map, wrapper } = mountMap()
    const old = fakeMap()
    const current = fakeMap()
    const afterReady = vi.fn<(ready: LeafletMap) => void>()
    map.bindMapReady(old)
    await nextTick()
    map.bindMapReady(current, afterReady)
    await nextTick()

    expect(observers[0]!.disconnect).toHaveBeenCalledOnce()
    frames[0]!(0)
    frames[1]!(0)
    expect(old.invalidateSize).not.toHaveBeenCalled()
    expect(current.invalidateSize).toHaveBeenCalledOnce()
    expect(afterReady).toHaveBeenCalledExactlyOnceWith(current)

    const observer = observers[1]!
    observer.callback([], observer)
    expect(current.invalidateSize).toHaveBeenCalledTimes(2)
    wrapper.unmount()
    expect(observer.disconnect).toHaveBeenCalledOnce()
    expect(map.mapInstance.value).toBeNull()
  })
})
