import { describe, it, expect, vi } from 'vitest'
import { defineComponent, h } from 'vue'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import TripRouteMap from '../TripRouteMap.vue'
import type { TripPoint } from '@/services/vehicleApi'

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
  return {
    LMap: stub('LMap', ['zoom', 'center', 'options']),
    LPolyline: stub('LPolyline', [
      'latLngs',
      'color',
      'className',
      'weight',
      'opacity',
      'dashArray',
      'interactive',
    ]),
    LCircleMarker: stub('LCircleMarker', ['latLng', 'radius', 'className', 'interactive']),
  }
})
vi.mock('@/composables/useBasemap', () => ({ useBasemap: () => undefined }))

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

  it('marks both ends', () => {
    const markers = mountMap().findAllComponents({ name: 'LCircleMarker' })

    expect(markers.map((m) => m.props('latLng'))).toEqual([
      [52.5, 6],
      [52.2, 6.1],
    ])
  })

  it('links to the trip on the full map, unless it is hidden from it', () => {
    const link = mountMap().find('.router-link')

    expect(JSON.parse(link.attributes('data-to')!)).toEqual({
      name: 'map',
      query: { trip: 7, at: '2026-09-24T12:32:00.000Z' },
    })
    expect(mountMap({ linkToMap: false }).find('.router-link').exists()).toBe(false)
  })
})
