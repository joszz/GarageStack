import { describe, it, expect, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { createPinia, setActivePinia } from 'pinia'
import en from '@/locales/en.json'
import type { EnergyUnit } from '@/utils/energy'
import type { MonthTotal } from '@/utils/statistics'
import StatsMonthTable from '../StatsMonthTable.vue'

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

const MONTHS: MonthTotal[] = [
  {
    key: '2026-08',
    start: new Date(2026, 7, 1),
    trips: 12,
    distanceKm: 1234.4,
    counterTotal: 4250,
  },
  { key: '2026-09', start: new Date(2026, 8, 1), trips: 3, distanceKm: 80.6, counterTotal: null },
]

function mountTable(energyUnit: EnergyUnit | null) {
  return mount(StatsMonthTable, {
    props: { months: MONTHS, energyUnit },
    global: { plugins: [i18n] },
  })
}

function cells(wrapper: ReturnType<typeof mountTable>, selector: string) {
  return wrapper.findAll(`${selector} > *`).map((cell) => cell.text())
}

describe('StatsMonthTable', () => {
  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
  })

  it('lists every month with its trips and distance, and a total', () => {
    const wrapper = mountTable(null)

    expect(cells(wrapper, 'thead tr')).toEqual(['Month', 'Trips', 'Distance (km)'])
    expect(cells(wrapper, 'tbody tr:first-child')[1]).toBe('12')
    expect(cells(wrapper, 'tbody tr:first-child')[2]).toBe('1,234')
    expect(cells(wrapper, 'tfoot tr')).toEqual(['Total', '15', '1,315'])
  })

  it('shows a hybrid counter as litres, and a month without readings as a dash', () => {
    const wrapper = mountTable('litres')

    expect(cells(wrapper, 'thead tr')[3]).toBe('Fuel (L)')
    expect(cells(wrapper, 'tbody tr:first-child')[3]).toBe('42.5')
    expect(cells(wrapper, 'tbody tr:last-child')[3]).toBe('-')
    expect(cells(wrapper, 'tfoot tr')[3]).toBe('42.5')
  })

  it('shows a plug-in counter in kWh as it is', () => {
    const wrapper = mountTable('kwh')

    expect(cells(wrapper, 'thead tr')[3]).toBe('Electric usage (kWh)')
    expect(cells(wrapper, 'tbody tr:first-child')[3]).toBe('4,250.0')
  })
})
