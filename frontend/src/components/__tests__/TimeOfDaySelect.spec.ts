import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import TimeOfDaySelect from '../TimeOfDaySelect.vue'

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

function mountSelect(modelValue: string) {
  return mount(TimeOfDaySelect, {
    props: { id: 'start', modelValue },
    global: { plugins: [i18n] },
  })
}

function lastValue(wrapper: ReturnType<typeof mountSelect>) {
  const emitted = wrapper.emitted('update:modelValue')!
  return emitted[emitted.length - 1]
}

describe('TimeOfDaySelect', () => {
  it('shows the time on a 24-hour clock, also in English', () => {
    const wrapper = mountSelect('19:05')
    const [hour, minute] = wrapper.findAll('select')

    expect(wrapper.findAll('select')).toHaveLength(2)
    expect(hour!.attributes('id')).toBe('start')
    expect((hour!.element as HTMLSelectElement).value).toBe('19')
    expect((minute!.element as HTMLSelectElement).value).toBe('5')
    const hours = hour!.findAll('option').map((o) => o.text())
    expect(hours).toHaveLength(24)
    expect([hours[0], hours[23]]).toEqual(['00', '23'])
  })

  it('writes back the time as HH:mm, whichever part changed', async () => {
    const wrapper = mountSelect('19:05')
    const [hour, minute] = wrapper.findAll('select')

    await hour!.setValue('7')
    expect(lastValue(wrapper)).toEqual(['07:05'])

    await wrapper.setProps({ modelValue: '07:05' })
    await minute!.setValue('45')
    expect(lastValue(wrapper)).toEqual(['07:45'])
  })

  it('offers minutes in steps of five, keeping a saved minute between them', () => {
    const [, minute] = mountSelect('07:32').findAll('select')

    expect(minute!.findAll('option').map((o) => o.text())).toEqual([
      '00',
      '05',
      '10',
      '15',
      '20',
      '25',
      '30',
      '32',
      '35',
      '40',
      '45',
      '50',
      '55',
    ])
  })
})
