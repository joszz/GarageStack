import { describe, it, expect, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { createPinia, setActivePinia } from 'pinia'
import TripLogItem from '../TripLogItem.vue'
import en from '@/locales/en.json'
import type { TripLogEntry } from '@/services/tripLogApi'
import { deventer, logEntry, zwolle } from '@/services/__tests__/tripLogFixtures'
import { useUiSettingsStore } from '@/stores/settingsUi'
import type { CommutePlace } from '@/services/commutePlacesApi'

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

beforeEach(() => {
  localStorage.clear()
  setActivePinia(createPinia())
})

function mountItem(
  entry: TripLogEntry,
  props: Partial<{
    placesShown: boolean
    placesResolving: boolean
    saving: boolean
    commutePlaces: CommutePlace[]
    selected: boolean
  }> = {},
) {
  return mount(TripLogItem, {
    props: {
      entry,
      placesShown: true,
      placesResolving: false,
      saving: false,
      commutePlaces: [],
      selected: false,
      ...props,
    },
    global: { plugins: [i18n], stubs: { FontAwesomeIcon: true } },
  })
}

describe('TripLogItem', () => {
  it('shows where the trip went, with postcodes', () => {
    const wrapper = mountItem(logEntry({ startPlace: zwolle, endPlace: deventer }))

    expect(wrapper.findAll('.trip-log-item__place').map((p) => p.text())).toEqual([
      'Grote Markt 1, 8011 PK Zwolle',
      'Brink 2, 7411 BT Deventer',
    ])
  })

  it('shows coordinates when place names are switched off, even for a known address', () => {
    const wrapper = mountItem(logEntry({ startPlace: zwolle, endPlace: deventer }), {
      placesShown: false,
    })

    expect(wrapper.find('.trip-log-item__place').text()).toBe('52.51230, 6.09210')
  })

  it('shows a placeholder for an address still being looked up', () => {
    const wrapper = mountItem(logEntry({ startPlace: zwolle }), { placesResolving: true })

    expect(wrapper.findAll('.skeleton')).toHaveLength(1)
    expect(wrapper.findAll('.trip-log-item__place')).toHaveLength(1)
  })

  it('shows how long the trip took', () => {
    expect(mountItem(logEntry()).find('.trip-log-item__duration').text()).toBe('34 min')
  })

  it('counts the trip for the distance on the odometer, and shows both readings', () => {
    const wrapper = mountItem(logEntry())

    expect(wrapper.find('.trip-log-item__km').text()).toBe('42.5 km')
    expect(wrapper.find('.trip-log-item__distance').text()).toContain('24010.0 - 24052.5')
  })

  it('shows the distance and the readings in miles for a browser set to miles', () => {
    useUiSettingsStore().units.distance = 'mi'
    const wrapper = mountItem(logEntry())

    expect(wrapper.find('.trip-log-item__km').text()).toBe('26.4 mi')
    expect(wrapper.find('.trip-log-item__distance').text()).toContain('14919.1 - 14945.5')
  })

  it('shows the purpose as a badge, and unclassified for a trip without one', () => {
    const commute = mountItem(logEntry({ purpose: 'commute' })).find('.trip-log-item__purpose')
    const none = mountItem(logEntry()).find('.trip-log-item__purpose')

    expect(commute.text()).toBe('Commute')
    expect(commute.classes()).toContain('badge-info')
    expect(none.text()).toBe('Unclassified')
    expect(none.classes()).toContain('badge-warning')
  })

  it('says when a commute was marked automatically, and when the route was edited', () => {
    const marked = mountItem(
      logEntry({ purpose: 'commute', purposeIsAutomatic: true, isEdited: true }),
    )
    const plain = mountItem(logEntry({ purpose: 'commute' }))

    expect(marked.find('.trip-log-item__auto').text()).toBe('Automatic')
    expect(marked.find('.trip-log-item__edited').text()).toBe(en.tripLog.edited)
    expect(plain.find('.trip-log-item__auto').exists()).toBe(false)
  })

  it('shows the notes the trip has', () => {
    expect(
      mountItem(logEntry({ notes: 'Client visit' }))
        .find('.trip-log-item__note')
        .text(),
    ).toBe('Client visit')
    expect(mountItem(logEntry()).find('.trip-log-item__note').exists()).toBe(false)
  })

  it('marks the end that is at home', () => {
    const home: CommutePlace = {
      id: 1,
      kind: 'home',
      label: null,
      latitude: 52.5123,
      longitude: 6.0921,
      radiusMeters: 250,
      notify: false,
    }
    const wrapper = mountItem(logEntry({ startPlace: zwolle, endPlace: deventer }), {
      commutePlaces: [home],
    })

    const [from, to] = wrapper.findAll('.trip-log-item__place')
    expect(from!.find('.trip-log-item__place-icon').attributes('icon')).toBe('house')
    expect(to!.find('.trip-log-item__place-icon').exists()).toBe(false)
  })

  it('is one button that picks the trip, pressed while the detail shows it', async () => {
    const wrapper = mountItem(logEntry())
    const selected = mountItem(logEntry(), { selected: true })

    expect(wrapper.findAll('button')).toHaveLength(1)
    await wrapper.find('button').trigger('click')

    expect(wrapper.emitted('select')).toHaveLength(1)
    expect(wrapper.find('button').attributes('aria-pressed')).toBe('false')
    expect(selected.find('button').attributes('aria-pressed')).toBe('true')
    expect(selected.classes()).toContain('trip-log-item--selected')
  })

  it('marks a hidden trip', () => {
    expect(mountItem(logEntry({ isHidden: true })).classes()).toContain('trip-log-item--hidden')
  })
})
