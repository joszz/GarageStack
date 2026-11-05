import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { createPinia, setActivePinia } from 'pinia'
import TripLogDetail from '../TripLogDetail.vue'
import en from '@/locales/en.json'
import type { TripLogEntry } from '@/services/tripLogApi'
import type { TripPoint } from '@/services/vehicleApi'
import { deventer, logEntry, zwolle } from '@/services/__tests__/tripLogFixtures'
import type { CommutePlace } from '@/services/commutePlacesApi'

// The map and the chart draw on canvases and tiles jsdom has none of; their own specs cover them.
vi.mock('../TripRouteMap.vue', () => ({
  default: {
    name: 'TripRouteMap',
    props: ['points', 'loading'],
    template: '<div class="route-map" />',
  },
}))
vi.mock('../StatsChartCard.vue', () => ({
  default: {
    name: 'StatsChartCard',
    props: ['title', 'data'],
    template: '<div class="speed-chart" />',
  },
}))

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

beforeEach(() => {
  localStorage.clear()
  setActivePinia(createPinia())
})

const fix = (speed: number | null): TripPoint => ({
  recordedAt: '2026-09-24T12:40:00.000Z',
  latitude: 52.4,
  longitude: 6.1,
  speed,
})

function mountDetail(
  entry: TripLogEntry,
  props: Partial<{
    routePoints: TripPoint[] | null
    routeError: string | null
    placesShown: boolean
    saving: boolean
    commutePlaces: CommutePlace[]
    canJoinNext: boolean
  }> = {},
) {
  return mount(TripLogDetail, {
    props: {
      entry,
      routePoints: null,
      routeLoading: false,
      routeError: null,
      placesShown: true,
      placesResolving: false,
      saving: false,
      commutePlaces: [],
      canJoinNext: true,
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

type Wrapper = ReturnType<typeof mountDetail>

function purposeButton(wrapper: Wrapper, label: string) {
  return wrapper.findAll('.trip-log-purpose').find((b) => b.text() === label)!
}

function button(wrapper: Wrapper, text: string) {
  return wrapper.findAll('button').find((b) => b.text() === text)
}

function figure(wrapper: Wrapper, label: string) {
  return wrapper
    .findAll('.trip-log-detail__figure')
    .find((f) => f.find('dt').text() === label)!
    .find('dd')
    .text()
    .replace(/\s+/g, '')
}

describe('TripLogDetail', () => {
  it('shows the distance, duration and speeds of the trip', () => {
    const wrapper = mountDetail(logEntry())

    expect(figure(wrapper, 'Distance')).toBe('42.5km')
    expect(figure(wrapper, 'Duration')).toBe('34min')
    expect(figure(wrapper, 'Avg speed')).toBe('72km/h')
    expect(figure(wrapper, 'Top speed')).toBe('104km/h')
  })

  it('shows the odometer at either end under the distance', () => {
    expect(mountDetail(logEntry()).find('.trip-log-detail__figure-note').text()).toBe(
      '24010.0 - 24052.5',
    )
    expect(
      mountDetail(logEntry({ odometerEndKm: null }))
        .find('.trip-log-detail__figure-note')
        .exists(),
    ).toBe(false)
  })

  it('names the purpose under the date', () => {
    const badge = mountDetail(logEntry({ purpose: 'private' })).find('.trip-log-detail__badge')

    expect(badge.text()).toBe('Private')
    expect(badge.classes()).toContain('badge-success')
  })

  it('shows a dash for speeds the car did not report', () => {
    const wrapper = mountDetail(logEntry({ maxSpeedKmh: null, avgMovingSpeedKmh: null }))

    expect(figure(wrapper, 'Top speed')).toBe('-')
  })

  it('draws the speed chart only when a fix carried a speed', () => {
    expect(
      mountDetail(logEntry(), { routePoints: [fix(null), fix(null)] })
        .find('.speed-chart')
        .exists(),
    ).toBe(false)
    expect(
      mountDetail(logEntry(), { routePoints: [fix(30), fix(50)] })
        .find('.speed-chart')
        .exists(),
    ).toBe(true)
  })

  it('says when the route could not be loaded', () => {
    const wrapper = mountDetail(logEntry(), { routeError: 'Offline' })

    expect(wrapper.text()).toContain(en.tripLog.detail.routeUnavailable)
  })

  it('offers each end of the trip to set as home or work', async () => {
    const wrapper = mountDetail(logEntry({ startPlace: zwolle, endPlace: deventer }))
    const ends = wrapper.findAll('.trip-log-detail__place')

    expect(ends.map((e) => e.text())).toEqual([
      'Grote Markt 1, 8011 PK Zwolle',
      'Brink 2, 7411 BT Deventer',
    ])
    await ends[1]!.trigger('click')

    expect(wrapper.emitted('pickPlace')).toEqual([[52.2554, 6.1639]])
  })

  it('marks the purpose the trip has', () => {
    const wrapper = mountDetail(logEntry({ purpose: 'commute' }))

    expect(purposeButton(wrapper, 'Commute').attributes('aria-pressed')).toBe('true')
    expect(purposeButton(wrapper, 'Business').attributes('aria-pressed')).toBe('false')
  })

  it('saves a purpose with the notes the trip already has', async () => {
    const wrapper = mountDetail(logEntry({ notes: 'Client visit' }))

    await purposeButton(wrapper, 'Business').trigger('click')

    expect(wrapper.emitted('save')).toEqual([['business', 'Client visit']])
  })

  it('clears the purpose when the one it has is chosen again', async () => {
    const wrapper = mountDetail(logEntry({ purpose: 'private' }))

    await purposeButton(wrapper, 'Private').trigger('click')

    expect(wrapper.emitted('save')).toEqual([[null, null]])
  })

  it('saves trimmed notes once the field changes, and a blank field as no notes', async () => {
    const wrapper = mountDetail(logEntry({ purpose: 'business', notes: 'Old' }))
    const notes = wrapper.find('input.trip-log-detail__notes')

    await notes.setValue('  New notes ')
    await notes.setValue('   ')

    expect(wrapper.emitted('save')).toEqual([
      ['business', 'New notes'],
      ['business', null],
    ])
  })

  it('does not save notes that did not change', async () => {
    const wrapper = mountDetail(logEntry({ notes: 'Same' }))

    await wrapper.find('input.trip-log-detail__notes').setValue('Same ')

    expect(wrapper.emitted('save')).toBeUndefined()
  })

  it('shows the next trip notes when another trip is picked', async () => {
    const wrapper = mountDetail(logEntry({ id: 1, notes: 'First' }))

    await wrapper.setProps({ entry: logEntry({ id: 2, notes: null }) })

    expect((wrapper.find('input.trip-log-detail__notes').element as HTMLInputElement).value).toBe(
      '',
    )
  })

  it('holds its controls while a change is being saved', () => {
    const wrapper = mountDetail(logEntry(), { saving: true })

    expect(
      wrapper.findAll('.trip-log-purpose').every((b) => b.attributes('disabled') !== undefined),
    ).toBe(true)
    expect(wrapper.find('input.trip-log-detail__notes').attributes('disabled')).toBeDefined()
  })

  it('hides a trip, and shows a hidden one again', async () => {
    const shown = mountDetail(logEntry())
    const hidden = mountDetail(logEntry({ isHidden: true }))

    await button(shown, 'Hide')!.trigger('click')
    await button(hidden, 'Show again')!.trigger('click')

    expect(shown.emitted('setHidden')).toEqual([[true]])
    expect(hidden.emitted('setHidden')).toEqual([[false]])
  })

  it('links to the trip on the map, but not for a hidden one the map leaves out', () => {
    const entry = logEntry()
    const mapLinks = (e: TripLogEntry) =>
      mountDetail(e)
        .findAll('.router-link')
        .filter((l) => JSON.parse(l.attributes('data-to')!).name === 'map')

    expect(JSON.parse(mapLinks(entry)[0]!.attributes('data-to')!)).toEqual({
      name: 'map',
      query: { trip: entry.id, at: entry.startedAt },
    })
    expect(mapLinks(logEntry({ isHidden: true }))).toHaveLength(0)
  })

  it('opens the route editor, for a hidden trip too', () => {
    const editLink = (entry: TripLogEntry) =>
      mountDetail(entry)
        .findAll('.router-link')
        .find((l) => JSON.parse(l.attributes('data-to')!).name === 'tripEdit')

    expect(JSON.parse(editLink(logEntry({ id: 9 }))!.attributes('data-to')!)).toEqual({
      name: 'tripEdit',
      params: { id: 9 },
    })
    expect(editLink(logEntry({ isHidden: true }))).toBeDefined()
  })

  it('asks to join with the next trip, but not for the newest', async () => {
    const wrapper = mountDetail(logEntry())

    await button(wrapper, 'Join with next trip')!.trigger('click')

    expect(wrapper.emitted('joinNext')).toHaveLength(1)
    expect(
      button(mountDetail(logEntry(), { canJoinNext: false }), 'Join with next trip'),
    ).toBeUndefined()
  })

  it('says when a commute was marked automatically', () => {
    const automatic = mountDetail(logEntry({ purpose: 'commute', purposeIsAutomatic: true }))
    const chosen = mountDetail(logEntry({ purpose: 'commute' }))

    expect(automatic.find('.trip-log-detail__mark').text()).toBe(en.tripLog.places.automaticHint)
    expect(chosen.find('.trip-log-detail__mark').exists()).toBe(false)
  })

  it('marks a trip whose route was edited', () => {
    expect(
      mountDetail(logEntry({ isEdited: true }))
        .find('.trip-log-detail__edited')
        .text(),
    ).toBe(en.tripLog.editedHint)
  })
})
