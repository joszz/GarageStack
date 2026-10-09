import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import nl from '@/locales/nl.json'
import TimeOfDaySelect from '../TimeOfDaySelect.vue'

function mountSelect(modelValue: string, locale: 'en' | 'nl' = 'en') {
  const i18n = createI18n({ legacy: false, locale, messages: { en, nl } })
  return mount(TimeOfDaySelect, {
    props: { id: 'start', modelValue },
    global: { plugins: [i18n] },
  })
}

function lastValue(wrapper: ReturnType<typeof mountSelect>) {
  const emitted = wrapper.emitted('update:modelValue')!
  return emitted[emitted.length - 1]
}

function selects(wrapper: ReturnType<typeof mountSelect>) {
  return wrapper.findAll('select')
}

function optionTexts(select: ReturnType<ReturnType<typeof mountSelect>['find']>) {
  return select.findAll('option').map((o) => o.text())
}

describe('TimeOfDaySelect', () => {
  it('shows an English time on a 12-hour clock, with AM or PM of its own', () => {
    const wrapper = mountSelect('19:05')
    const [hour, minute, period] = selects(wrapper)

    expect(selects(wrapper)).toHaveLength(3)
    expect(hour!.attributes('id')).toBe('start')
    expect((hour!.element as HTMLSelectElement).value).toBe('7')
    expect((minute!.element as HTMLSelectElement).value).toBe('5')
    expect((period!.element as HTMLSelectElement).value).toBe('pm')
    expect(optionTexts(hour!)[0]).toBe('12')
    expect(optionTexts(period!)).toEqual(['AM', 'PM'])
  })

  it('writes back a 24-hour time, whichever part changed', async () => {
    const wrapper = mountSelect('19:05')
    const [hour, minute, period] = selects(wrapper)

    await period!.setValue('am')
    expect(lastValue(wrapper)).toEqual(['07:05'])

    await wrapper.setProps({ modelValue: '07:05' })
    await hour!.setValue('12')
    expect(lastValue(wrapper)).toEqual(['00:05'])

    await wrapper.setProps({ modelValue: '00:05' })
    await minute!.setValue('45')
    expect(lastValue(wrapper)).toEqual(['00:45'])
  })

  it('offers minutes in steps of five, keeping a saved minute between them', () => {
    const [, minute] = selects(mountSelect('07:32'))

    expect(optionTexts(minute!)).toEqual([
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

  it('shows a Dutch time on a 24-hour clock, without AM or PM', () => {
    const wrapper = mountSelect('19:05', 'nl')
    const [hour] = selects(wrapper)

    expect(selects(wrapper)).toHaveLength(2)
    expect((hour!.element as HTMLSelectElement).value).toBe('19')
    expect(optionTexts(hour!)).toHaveLength(24)
  })
})
