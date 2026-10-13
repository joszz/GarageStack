import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { createPinia, setActivePinia } from 'pinia'
import en from '@/locales/en.json'
import { useUiSettingsStore } from '@/stores/settingsUi'
import StatsPeriodFilter from '../StatsPeriodFilter.vue'

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

function mountFilter() {
  return mount(StatsPeriodFilter, {
    global: { plugins: [i18n], stubs: { FontAwesomeIcon: true } },
  })
}

describe('StatsPeriodFilter', () => {
  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(new Date(2026, 9, 13, 15, 30))
  })

  afterEach(() => vi.useRealTimers())

  it('offers every period and stores the one chosen', async () => {
    const wrapper = mountFilter()
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

    await wrapper.find('select').setValue('lastYear')
    expect(useUiSettingsStore().statsPeriod).toEqual({ preset: 'lastYear', from: null, to: null })
    expect(wrapper.find('input[type="date"]').exists()).toBe(false)
  })

  it('starts custom dates from the days on screen', async () => {
    const store = useUiSettingsStore()
    store.statsPeriod = { preset: 'lastMonth', from: null, to: null }
    const wrapper = mountFilter()

    await wrapper.find('select').setValue('custom')

    expect(store.statsPeriod).toEqual({ preset: 'custom', from: '2026-09-01', to: '2026-09-30' })
    const [from, to] = wrapper.findAll<HTMLInputElement>('input[type="date"]')
    expect(from!.element.value).toBe('2026-09-01')
    expect(to!.element.value).toBe('2026-09-30')
    expect(to!.attributes('max')).toBe('2026-10-13')
  })

  it('moves one end of a custom period, and keeps it when the field is emptied', async () => {
    const store = useUiSettingsStore()
    store.statsPeriod = { preset: 'custom', from: '2026-09-01', to: '2026-09-30' }
    const wrapper = mountFilter()
    const from = wrapper.find('input[type="date"]')

    await from.setValue('2026-01-01')
    expect(store.statsPeriod.from).toBe('2026-01-01')

    await from.setValue('')
    expect(store.statsPeriod.from).toBe('2026-01-01')
  })
})
