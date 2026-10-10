import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import WeekdayPicker from '../WeekdayPicker.vue'
import type { IsoWeekday } from '@/services/climateScheduleApi'

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

function mountPicker(modelValue: IsoWeekday[]) {
  return mount(WeekdayPicker, {
    props: { modelValue, groupLabel: 'Repeat' },
    global: { plugins: [i18n], stubs: { FontAwesomeIcon: true } },
  })
}

function button(wrapper: ReturnType<typeof mountPicker>, label: string) {
  return wrapper.findAll('button').find((b) => b.text() === label)!
}

describe('WeekdayPicker', () => {
  it('shows the seven days from Monday, pressed for the chosen ones', () => {
    const wrapper = mountPicker([1, 5])
    const chips = wrapper.findAll('.toggle-chip')

    expect(chips.map((c) => c.text())).toEqual(['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'])
    expect(chips.map((c) => c.attributes('aria-pressed'))).toEqual([
      'true',
      'false',
      'false',
      'false',
      'true',
      'false',
      'false',
    ])
  })

  it('adds a day in weekday order', async () => {
    const wrapper = mountPicker([5])

    await button(wrapper, 'Wed').trigger('click')

    expect(wrapper.emitted('update:modelValue')).toEqual([[[3, 5]]])
  })

  it('sets the common choices in one tap', async () => {
    const wrapper = mountPicker([])

    await button(wrapper, 'Weekdays').trigger('click')
    await button(wrapper, 'Weekend').trigger('click')
    await button(wrapper, 'Every day').trigger('click')

    expect(wrapper.emitted('update:modelValue')).toEqual([
      [[1, 2, 3, 4, 5]],
      [[6, 7]],
      [[1, 2, 3, 4, 5, 6, 7]],
    ])
  })
})
