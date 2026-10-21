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
    canJoinNext: boolean
  }> = {},
) {
  return mount(TripLogItem, {
    props: {
      entry,
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

function purposeButton(wrapper: ReturnType<typeof mountItem>, label: string) {
  return wrapper.findAll('.trip-log-purpose').find((b) => b.text() === label)!
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

  it('marks the purpose the trip has', () => {
    const wrapper = mountItem(logEntry({ purpose: 'commute' }))

    expect(purposeButton(wrapper, 'Commute').attributes('aria-pressed')).toBe('true')
    expect(purposeButton(wrapper, 'Business').attributes('aria-pressed')).toBe('false')
  })

  it('saves a purpose with the notes the trip already has', async () => {
    const wrapper = mountItem(logEntry({ notes: 'Client visit' }))

    await purposeButton(wrapper, 'Business').trigger('click')

    expect(wrapper.emitted('save')).toEqual([['business', 'Client visit']])
  })

  it('clears the purpose when the one it has is chosen again', async () => {
    const wrapper = mountItem(logEntry({ purpose: 'private' }))

    await purposeButton(wrapper, 'Private').trigger('click')

    expect(wrapper.emitted('save')).toEqual([[null, null]])
  })

  it('saves trimmed notes once the field changes, and a blank field as no notes', async () => {
    const wrapper = mountItem(logEntry({ purpose: 'business', notes: 'Old' }))
    const notes = wrapper.find('input.trip-log-item__notes')

    await notes.setValue('  New notes ')
    await notes.setValue('   ')

    expect(wrapper.emitted('save')).toEqual([
      ['business', 'New notes'],
      ['business', null],
    ])
  })

  it('does not save notes that did not change', async () => {
    const wrapper = mountItem(logEntry({ notes: 'Same' }))

    await wrapper.find('input.trip-log-item__notes').setValue('Same ')

    expect(wrapper.emitted('save')).toBeUndefined()
  })

  it('holds its controls while a change is being saved', () => {
    const wrapper = mountItem(logEntry(), { saving: true })

    expect(
      wrapper.findAll('.trip-log-purpose').every((b) => b.attributes('disabled') !== undefined),
    ).toBe(true)
    expect(wrapper.find('input.trip-log-item__notes').attributes('disabled')).toBeDefined()
  })

  // ── Home and work ──────────────────────────────────────────

  const home: CommutePlace = {
    id: 1,
    kind: 'home',
    label: null,
    latitude: 52.5123,
    longitude: 6.0921,
    radiusMeters: 250,
    notify: false,
  }

  it('marks the end that is at home', () => {
    const wrapper = mountItem(logEntry({ startPlace: zwolle, endPlace: deventer }), {
      commutePlaces: [home],
    })

    const [from, to] = wrapper.findAll('.trip-log-item__place')
    expect(from!.find('.trip-log-item__place-icon').attributes('icon')).toBe('house')
    expect(to!.find('.trip-log-item__place-icon').exists()).toBe(false)
  })

  it('offers a trip end to set as home or work', async () => {
    const wrapper = mountItem(logEntry({ startPlace: zwolle, endPlace: deventer }))

    await wrapper.findAll('.trip-log-item__place')[1]!.trigger('click')

    expect(wrapper.emitted('pickPlace')).toEqual([[52.2554, 6.1639]])
  })

  it('hides a trip, and shows a hidden one again', async () => {
    const shown = mountItem(logEntry())
    const hidden = mountItem(logEntry({ isHidden: true }))

    await shown
      .findAll('button')
      .find((b) => b.text() === 'Hide')!
      .trigger('click')
    await hidden
      .findAll('button')
      .find((b) => b.text() === 'Show again')!
      .trigger('click')

    expect(shown.emitted('setHidden')).toEqual([[true]])
    expect(hidden.emitted('setHidden')).toEqual([[false]])
    expect(hidden.classes()).toContain('trip-log-item--hidden')
  })

  it('links to the trip on the map, but not for a hidden one the map leaves out', () => {
    const entry = logEntry()
    const link = mountItem(entry).find('.router-link')

    expect(link.text()).toBe(en.tripLog.showOnMap)
    expect(JSON.parse(link.attributes('data-to')!)).toEqual({
      name: 'map',
      query: { trip: entry.id, at: entry.startedAt },
    })
    expect(
      mountItem(logEntry({ isHidden: true }))
        .find('.router-link')
        .exists(),
    ).toBe(false)
  })

  it('joins with the next trip only on a second tap', async () => {
    const wrapper = mountItem(logEntry())
    const join = () =>
      wrapper
        .findAll('button')
        .find((b) => b.text().startsWith('Join') || b.text().startsWith('Tap'))!

    await join().trigger('click')
    expect(wrapper.emitted('joinNext')).toBeUndefined()
    expect(join().text()).toBe('Tap again to join')

    await join().trigger('click')
    expect(wrapper.emitted('joinNext')).toHaveLength(1)
  })

  it('offers no join for the newest trip', () => {
    const wrapper = mountItem(logEntry(), { canJoinNext: false })

    expect(wrapper.findAll('button').some((b) => b.text() === 'Join with next trip')).toBe(false)
  })

  it('says when a commute was marked automatically', () => {
    const automatic = mountItem(logEntry({ purpose: 'commute', purposeIsAutomatic: true }))
    const chosen = mountItem(logEntry({ purpose: 'commute' }))

    expect(automatic.find('.trip-log-item__auto').text()).toBe('Automatic')
    expect(chosen.find('.trip-log-item__auto').exists()).toBe(false)
  })
})
