import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import TripLogItem from '../TripLogItem.vue'
import en from '@/locales/en.json'
import type { TripLogEntry } from '@/services/tripLogApi'
import { deventer, logEntry, zwolle } from '@/services/__tests__/tripLogFixtures'

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

function mountItem(
  entry: TripLogEntry,
  props: Partial<{ placesShown: boolean; placesResolving: boolean; saving: boolean }> = {},
) {
  return mount(TripLogItem, {
    props: {
      entry,
      locale: 'en-US',
      placesShown: true,
      placesResolving: false,
      saving: false,
      ...props,
    },
    global: { plugins: [i18n], stubs: { FontAwesomeIcon: true } },
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
})
