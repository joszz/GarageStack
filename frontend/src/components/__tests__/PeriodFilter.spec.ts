import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import PeriodFilter from '../PeriodFilter.vue'
import { MAP_PERIOD, STATS_PERIOD, type Period, type PeriodOptions } from '@/utils/period'

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

function mountFilter(period: Period, options: PeriodOptions = STATS_PERIOD) {
  const wrapper = mount(PeriodFilter, {
    props: {
      modelValue: period,
      options,
      desc: 'What the period decides',
      'onUpdate:modelValue': (value: Period) => wrapper.setProps({ modelValue: value }),
    },
    global: { plugins: [i18n], stubs: { FontAwesomeIcon: true } },
  })
  return wrapper
}

function current(wrapper: ReturnType<typeof mountFilter>): Period {
  return wrapper.props('modelValue')
}

describe('PeriodFilter', () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(new Date(2026, 9, 13, 15, 30))
  })

  afterEach(() => vi.useRealTimers())

  it('offers every period the page can load and hands back the one chosen', async () => {
    const wrapper = mountFilter(STATS_PERIOD.fallback)
    const options = wrapper.findAll('option').map((o) => o.text())

    expect(options).toEqual([
      'Last 7 days',
      'Last 30 days',
      'Last 90 days',
      'This month',
      'Last month',
      'This year',
      'Last year',
      'Custom dates',
    ])
    expect(wrapper.text()).toContain('What the period decides')

    await wrapper.find('select').setValue('lastYear')
    expect(current(wrapper)).toEqual({ preset: 'lastYear', from: null, to: null })
    expect(wrapper.find('input[type="date"]').exists()).toBe(false)
  })

  it('leaves out the periods longer than the map can draw', () => {
    const wrapper = mountFilter(MAP_PERIOD.fallback, MAP_PERIOD)

    expect(wrapper.findAll('option').map((o) => o.text())).toEqual([
      'Last 7 days',
      'Last 30 days',
      'Last 90 days',
      'This month',
      'Last month',
      'Custom dates',
    ])
  })

  it('starts custom dates from the days on screen', async () => {
    const wrapper = mountFilter({ preset: 'lastMonth', from: null, to: null })

    await wrapper.find('select').setValue('custom')

    expect(current(wrapper)).toEqual({ preset: 'custom', from: '2026-09-01', to: '2026-09-30' })
    const [from, to] = wrapper.findAll<HTMLInputElement>('input[type="date"]')
    expect(from!.element.value).toBe('2026-09-01')
    expect(to!.element.value).toBe('2026-09-30')
    expect(to!.attributes('max')).toBe('2026-10-13')
    expect(wrapper.find('.period-filter__limit').exists()).toBe(false)
  })

  it('starts custom dates within what the page can load', async () => {
    const wrapper = mountFilter({ preset: 'last90', from: null, to: null }, MAP_PERIOD)

    await wrapper.find('select').setValue('custom')

    expect(current(wrapper)).toEqual({ preset: 'custom', from: '2026-07-16', to: '2026-10-13' })
    expect(wrapper.get('.period-filter__limit').text()).toBe('At most 90 days.')
  })

  it('moves one end of a custom period, and keeps it when the field is emptied', async () => {
    const wrapper = mountFilter({ preset: 'custom', from: '2026-09-01', to: '2026-09-30' })
    const from = wrapper.find('input[type="date"]')

    await from.setValue('2026-01-01')
    expect(current(wrapper).from).toBe('2026-01-01')

    await from.setValue('')
    expect(current(wrapper).from).toBe('2026-01-01')
  })

  it('takes the other end along when one moves further than the page can load', async () => {
    const wrapper = mountFilter(
      { preset: 'custom', from: '2026-09-01', to: '2026-09-30' },
      MAP_PERIOD,
    )
    const [from, to] = wrapper.findAll('input[type="date"]')

    await from!.setValue('2026-01-01')
    expect(current(wrapper)).toEqual({ preset: 'custom', from: '2026-01-01', to: '2026-03-31' })

    await to!.setValue('2026-10-13')
    expect(current(wrapper)).toEqual({ preset: 'custom', from: '2026-07-16', to: '2026-10-13' })
  })
})
