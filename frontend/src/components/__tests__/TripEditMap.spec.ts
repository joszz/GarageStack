import { describe, it, expect, vi } from 'vitest'
import { defineComponent, h } from 'vue'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import TripEditMap from '../TripEditMap.vue'
import type { TripPoint } from '@/services/vehicleApi'

// The map itself is Leaflet's business; these stand-ins keep its props and events in reach. Only
// a point passes a click on its element on, as Leaflet keeps a marker's click off the map.
vi.mock('@vue-leaflet/vue-leaflet', () => {
  const stub = (name: string, props: string[]) =>
    defineComponent({
      name,
      props,
      inheritAttrs: false,
      setup:
        (_, { slots, attrs }) =>
        () =>
          h(
            'div',
            name === 'LCircleMarker'
              ? { onClick: (e: Event) => (e.stopPropagation(), (attrs.onClick as () => void)()) }
              : {},
            slots.default?.(),
          ),
    })
  return {
    LMap: stub('LMap', ['zoom', 'center']),
    LPolyline: stub('LPolyline', ['latLngs', 'className', 'weight', 'dashArray', 'interactive']),
    LCircleMarker: stub('LCircleMarker', [
      'latLng',
      'radius',
      'className',
      'weight',
      'interactive',
      'bubblingMouseEvents',
    ]),
    LMarker: stub('LMarker', ['latLng', 'title', 'draggable']),
    LIcon: stub('LIcon', ['iconSize', 'iconAnchor', 'className']),
  }
})
vi.mock('@/composables/useBasemap', () => ({ useBasemap: () => undefined }))

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

const fix = (latitude: number, source?: TripPoint['source']): TripPoint => ({
  recordedAt: '2026-01-01T10:00:00.000Z',
  latitude,
  longitude: 5,
  speed: null,
  ...(source ? { source } : {}),
})
const recorded = [fix(52.0), fix(52.05), fix(52.1)]

function mountMap(props: Partial<InstanceType<typeof TripEditMap>['$props']> = {}) {
  return mount(TripEditMap, {
    props: {
      points: recorded,
      recorded,
      mode: 'start',
      selected: null,
      previousStop: null,
      nextStop: null,
      ...props,
    },
    global: { plugins: [i18n] },
  })
}

const lines = (wrapper: ReturnType<typeof mountMap>) =>
  wrapper.findAllComponents({ name: 'LPolyline' }).map((l) => l.props('className'))

describe('TripEditMap', () => {
  it('draws the route alone while it is as recorded', () => {
    expect(lines(mountMap())).toEqual(['trip-edit-line'])
  })

  it('draws the recorded route faintly under an edited one, with the placed stretch dashed', () => {
    const edited = [fix(51.99, 'manual'), ...recorded]

    expect(lines(mountMap({ points: edited }))).toEqual([
      'trip-edit-line trip-edit-line--recorded',
      'trip-edit-line',
      'trip-added-line',
    ])
  })

  it('marks the end a tap moves', () => {
    const icons = mountMap({ mode: 'end' })
      .findAllComponents({ name: 'LIcon' })
      .map((i) => i.props('className'))

    expect(icons).toEqual([
      'trip-edit-marker trip-edit-marker--start',
      'trip-edit-marker trip-edit-marker--end trip-edit-marker--active',
    ])
  })

  it('shows the points between the ends only in points mode, and picks one on a tap', async () => {
    expect(mountMap().findAllComponents({ name: 'LCircleMarker' })).toHaveLength(0)

    const wrapper = mountMap({ mode: 'points', selected: 1 })
    const points = wrapper.findAllComponents({ name: 'LCircleMarker' })
    expect(points).toHaveLength(1)
    expect(points[0]!.props('className')).toBe('trip-edit-point trip-edit-point--selected')

    await points[0]!.trigger('click')
    expect(wrapper.emitted('selectPoint')).toEqual([[1]])
  })

  it('passes a tap on the map on, wherever it was', async () => {
    const wrapper = mountMap()

    wrapper.findComponent({ name: 'LMap' }).vm.$emit('click', { latlng: { lat: 52.2, lng: 5.1 } })

    expect(wrapper.emitted('place')).toEqual([[52.2, 5.1]])
  })

  it('passes where a dragged end was dropped', () => {
    const wrapper = mountMap()
    const [start, end] = wrapper.findAllComponents({ name: 'LMarker' })
    const dropped = (lat: number, lng: number) => ({ target: { getLatLng: () => ({ lat, lng }) } })

    start!.vm.$emit('dragend', dropped(51.99, 5))
    end!.vm.$emit('dragend', dropped(52.11, 5))

    expect(wrapper.emitted('moveStart')).toEqual([[51.99, 5]])
    expect(wrapper.emitted('moveEnd')).toEqual([[52.11, 5]])
  })
})
