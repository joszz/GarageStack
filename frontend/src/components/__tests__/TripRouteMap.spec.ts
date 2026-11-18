import { describe, it, expect, vi, afterEach } from 'vitest'
import { defineComponent, h, onMounted } from 'vue'
import { flushPromises, mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import TripRouteMap from '../TripRouteMap.vue'
import type { TripPoint } from '@/services/vehicleApi'
import { fitToPoints } from '@/utils/leaflet'

// The map itself is Leaflet's business; these stand-ins keep its props in reach.
vi.mock('@vue-leaflet/vue-leaflet', () => {
  const stub = (name: string, props: string[]) =>
    defineComponent({
      name,
      props,
      inheritAttrs: false,
      setup:
        (_, { slots }) =>
        () =>
          h('div', slots.default?.()),
    })
  // The map hands a fake Leaflet map to @ready once mounted, as vue-leaflet does, so the
  // framing can be watched; `fire` stands in for Leaflet raising an event on it.
  const handlers: Record<string, () => void> = {}
  // Every point lands at `projected` in the map's container, which sits at the page's top left.
  const fakeMap = {
    projected: { x: 0, y: 200 },
    getContainer: () => ({ getBoundingClientRect: () => ({ left: 0, top: 0 }) }),
    latLngToContainerPoint: () => fakeMap.projected,
    invalidateSize: () => {},
    on: (event: string, handler: () => void) => {
      handlers[event] = handler
    },
    fire: (event: string) => handlers[event]?.(),
  }
  const LMap = defineComponent({
    name: 'LMap',
    props: ['zoom', 'center', 'options'],
    emits: ['ready'],
    setup(_, { slots, emit }) {
      onMounted(() => emit('ready', fakeMap))
      return () => h('div', slots.default?.())
    },
  })
  return {
    fakeMap,
    LMap,
    LPolyline: stub('LPolyline', [
      'latLngs',
      'color',
      'className',
      'weight',
      'opacity',
      'dashArray',
      'interactive',
    ]),
  }
})
vi.mock('@/composables/useBasemap', () => ({ useBasemap: () => undefined }))
// The end markers are added through Leaflet itself; each records where it stands and the HTML of
// its icon, which is handed through as the options it was built from.
interface FakeMarker {
  latLng: [number, number]
  html: string
}
const markers = vi.hoisted((): FakeMarker[] => [])
vi.mock('@/utils/leaflet', () => ({
  fitToPoints: vi.fn<typeof fitToPoints>(),
  L: {
    divIcon: (options: { html: string }) => options,
    marker: (latLng: [number, number], options: { icon: { html: string } }) => {
      const marker = {
        latLng,
        html: options.icon.html,
        addTo: () => marker,
        setLatLng: (next: [number, number]) => {
          marker.latLng = next
        },
      }
      markers.push(marker)
      return marker
    },
  },
}))

// jsdom has no layout, so neither ResizeObserver nor a frame to wait for.
vi.stubGlobal(
  'ResizeObserver',
  class {
    observe() {}
    disconnect() {}
  },
)
vi.stubGlobal('requestAnimationFrame', (callback: () => void) => {
  callback()
  return 0
})

afterEach(() => {
  vi.mocked(fitToPoints).mockClear()
  markers.length = 0
})

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

const fix = (latitude: number, speed: number | null): TripPoint => ({
  recordedAt: '2026-09-24T12:40:00.000Z',
  latitude,
  longitude: 6,
  speed,
})

function mountMap(props: Partial<InstanceType<typeof TripRouteMap>['$props']> = {}) {
  return mount(TripRouteMap, {
    props: {
      points: null,
      loading: false,
      start: [52.5, 6] as [number, number],
      end: [52.2, 6.1] as [number, number],
      tripId: 7,
      startedAt: '2026-09-24T12:32:00.000Z',
      linkToMap: true,
      ...props,
    },
    global: {
      plugins: [i18n],
      stubs: {
        FontAwesomeIcon: true,
        RouterLink: {
          props: ['to'],
          template: '<a class="router-link" :data-to="JSON.stringify(to)"><slot /></a>',
        },
      },
    },
  })
}

const lines = (wrapper: ReturnType<typeof mountMap>) =>
  wrapper.findAllComponents({ name: 'LPolyline' })

describe('TripRouteMap', () => {
  it('keeps the centre it opened on when the detail hands the start again', async () => {
    const wrapper = mountMap()
    const center = () => wrapper.findComponent({ name: 'LMap' }).props('center')
    const opened = center()

    // vue-leaflet sets the view again on a new centre, which undid the framing.
    await wrapper.setProps({ start: [52.5, 6] })

    expect(center()).toBe(opened)
  })

  it('zooms in quarter steps, so the trip fills the preview', () => {
    const map = mountMap().findComponent({ name: 'LMap' })

    expect(map.props('options')).toMatchObject({ zoomSnap: 0.25, dragging: false })
  })

  it('distinguishes confirmed phone stretches and names them in a legend', () => {
    const wrapper = mountMap({
      points: [fix(52.5, 40), { ...fix(52.4, 40), source: 'phone' }, fix(52.3, 40)],
    })
    const phone = lines(wrapper).find((line) => line.props('className') === 'trip-phone-line')
    expect(phone?.props('dashArray')).toBe('2 8')
    expect(phone?.props('latLngs')).toEqual([
      [52.5, 6],
      [52.4, 6],
      [52.3, 6],
    ])
    expect(wrapper.text()).toContain(en.dawarich.phonePoints)
  })
  it('colours the route by the speed of each stretch', () => {
    const wrapper = mountMap({ points: [fix(52.5, 20), fix(52.4, 140), fix(52.3, 140)] })

    const colours = lines(wrapper).map((l) => l.props('color'))
    expect(colours).toHaveLength(2)
    expect(colours[0]).not.toBe(colours[1])
  })

  it('joins the ends with a straight line once the fixes could not be had', () => {
    const wrapper = mountMap()

    expect(lines(wrapper)).toHaveLength(1)
    expect(lines(wrapper)[0]!.props('latLngs')).toEqual([
      [52.5, 6],
      [52.2, 6.1],
    ])
  })

  it('draws no line while the fixes load, and says it is loading', () => {
    const wrapper = mountMap({ loading: true })

    expect(lines(wrapper)).toHaveLength(0)
    expect(wrapper.find('[role="status"]').exists()).toBe(true)
  })

  it('marks the start with the pulsing dot and the end with the flag, as the map page does', async () => {
    mountMap()
    await flushPromises()

    expect(markers.map((m) => m.latLng)).toEqual([
      [52.5, 6],
      [52.2, 6.1],
    ])
    expect(markers[0]!.html).toContain('trip-marker--start')
    expect(markers[1]!.html).toContain('trip-flag-flag')
  })

  it('moves the same two markers when the ends change, rather than adding more', async () => {
    const wrapper = mountMap()
    await flushPromises()

    await wrapper.setProps({ end: [52.1, 6.2] })

    expect(markers).toHaveLength(2)
    expect(markers[1]!.latLng).toEqual([52.1, 6.2])
  })

  const targets = (wrapper: ReturnType<typeof mountMap>) =>
    wrapper.findAll('.router-link').map((link) => JSON.parse(link.attributes('data-to')!))

  it('links to the route editor and to the trip on the full map, side by side', () => {
    expect(targets(mountMap())).toEqual([
      { name: 'tripEdit', params: { id: 7 } },
      { name: 'map', query: { trip: 7, at: '2026-09-24T12:32:00.000Z' } },
    ])
  })

  it('still offers the editor for a trip hidden from the map, but not the map', () => {
    expect(targets(mountMap({ linkToMap: false }))).toEqual([
      { name: 'tripEdit', params: { id: 7 } },
    ])
  })

  const leaflet = async () =>
    (await import('@vue-leaflet/vue-leaflet')) as unknown as {
      fakeMap: { fire: (event: string) => void; projected: { x: number; y: number } }
    }

  // The buttons' corner: 340 to 420 px across and down to 42 px, in a 420 px wide preview.
  function buttonsAt(wrapper: ReturnType<typeof mountMap>) {
    vi.spyOn(
      wrapper.get('.trip-route-map__tools').element,
      'getBoundingClientRect',
    ).mockReturnValue({
      left: 340,
      right: 420,
      top: 8,
      bottom: 42,
      width: 80,
      height: 34,
    } as DOMRect)
  }

  it('frames the route and both ends as closely as the flag and attribution allow, again on a resize', async () => {
    const { fakeMap } = await leaflet()
    fakeMap.projected = { x: 100, y: 200 }
    mountMap({ points: [fix(52.5, 20), fix(52.2, 60)] })
    await flushPromises()

    expect(fitToPoints).toHaveBeenCalledTimes(1)
    const [, framed, padding] = vi.mocked(fitToPoints).mock.calls[0]!
    // The fixes, then the trip's own start and end, which need not be the first and last fix.
    expect(framed).toEqual([
      [52.5, 6],
      [52.2, 6],
      [52.5, 6],
      [52.2, 6.1],
    ])
    expect(padding).toEqual({ topLeft: [24, 40], bottomRight: [32, 28] })

    fakeMap.fire('resize')

    expect(fitToPoints).toHaveBeenCalledTimes(2)
  })

  it('frames it again clear of the buttons only when part of it lands under them', async () => {
    const { fakeMap } = await leaflet()
    const wrapper = mountMap({ points: [fix(52.5, 20), fix(52.2, 60)] })
    await flushPromises()
    buttonsAt(wrapper)

    // Well below the buttons: the close framing stays.
    fakeMap.projected = { x: 380, y: 150 }
    fakeMap.fire('resize')
    expect(vi.mocked(fitToPoints).mock.lastCall![2]).toEqual({
      topLeft: [24, 40],
      bottomRight: [32, 28],
    })

    // A flag standing just under them reaches into them: framed again with their height free.
    vi.mocked(fitToPoints).mockClear()
    fakeMap.projected = { x: 380, y: 60 }
    fakeMap.fire('resize')
    expect(vi.mocked(fitToPoints).mock.calls.map((call) => call[2])).toEqual([
      { topLeft: [24, 40], bottomRight: [32, 28] },
      { topLeft: [24, 56], bottomRight: [32, 28] },
    ])
  })
})
