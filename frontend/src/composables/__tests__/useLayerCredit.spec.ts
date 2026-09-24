import { describe, it, expect, vi } from 'vitest'
import { defineComponent, h, ref, shallowRef, nextTick } from 'vue'
import { mount } from '@vue/test-utils'
import type { LeafletMap } from '@/utils/leaflet'
import { useLayerCredit } from '../useLayerCredit'

const CREDIT = '<a href="https://openchargemap.org">Open Charge Map</a>'

function fakeMap(withControl = true) {
  const added: string[] = []
  const removed: string[] = []
  const map = {
    attributionControl: withControl
      ? {
          addAttribution: (text: string) => added.push(text),
          removeAttribution: (text: string) => removed.push(text),
        }
      : undefined,
  } as unknown as LeafletMap
  return { map, added, removed }
}

function mountCredit(map: LeafletMap | null, active = true) {
  const mapRef = shallowRef<LeafletMap | null>(map)
  const activeRef = ref(active)
  const wrapper = mount(
    defineComponent({
      setup() {
        useLayerCredit(mapRef, activeRef, CREDIT)
        return () => h('div')
      },
    }),
  )
  return { mapRef, activeRef, wrapper }
}

describe('useLayerCredit', () => {
  it('credits the source while the layer is on', () => {
    const { map, added } = fakeMap()
    mountCredit(map)

    expect(added).toEqual([CREDIT])
  })

  it('takes the credit down when the layer goes off, and puts it back up again', async () => {
    const { map, added, removed } = fakeMap()
    const { activeRef } = mountCredit(map)

    activeRef.value = false
    await nextTick()
    expect(removed).toEqual([CREDIT])

    activeRef.value = true
    await nextTick()
    expect(added).toEqual([CREDIT, CREDIT])
  })

  it('does not credit a source for a layer that never came on', () => {
    const { map, added } = fakeMap()
    mountCredit(map, false)

    expect(added).toEqual([])
  })

  it('adds the credit only once while the layer stays on', async () => {
    const { map, added } = fakeMap()
    const { activeRef } = mountCredit(map)

    activeRef.value = true
    await nextTick()

    expect(added).toEqual([CREDIT])
  })

  it('moves the credit with the map when the view swaps its instance', async () => {
    const first = fakeMap()
    const second = fakeMap()
    const { mapRef } = mountCredit(first.map)

    mapRef.value = second.map
    await nextTick()

    expect(first.removed).toEqual([CREDIT])
    expect(second.added).toEqual([CREDIT])
  })

  it('drops the credit on unmount, so a later map does not inherit it', () => {
    const { map, removed } = fakeMap()
    const { wrapper } = mountCredit(map)

    wrapper.unmount()

    expect(removed).toEqual([CREDIT])
  })

  it('leaves a map without an attribution control alone', () => {
    const { map } = fakeMap(false)
    const warn = vi.spyOn(console, 'error').mockImplementation(() => {})

    expect(() => mountCredit(map)).not.toThrow()

    warn.mockRestore()
  })
})
