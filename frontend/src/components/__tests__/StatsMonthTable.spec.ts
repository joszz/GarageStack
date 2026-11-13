import { describe, it, expect, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { createPinia, setActivePinia } from 'pinia'
import en from '@/locales/en.json'
import type { EnergyUnit } from '@/utils/energy'
import type { MonthTotal } from '@/utils/statistics'
import StatsMonthTable from '../StatsMonthTable.vue'

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

// The first month's counter over the days that also counted a distance: 4,250 hundredths of a
// litre (42.5 L) or 180 kWh over 1,200 km. The second month drove too little to say.
function months(usedCounter: number): MonthTotal[] {
  return [
    {
      key: '2026-08',
      start: new Date(2026, 7, 1),
      trips: 12,
      distanceKm: 1234.4,
      counterTotal: 4250,
      used: { counter: usedCounter, distanceKm: 1200 },
    },
    {
      key: '2026-09',
      start: new Date(2026, 8, 1),
      trips: 3,
      distanceKm: 80.6,
      counterTotal: null,
      used: { counter: 0, distanceKm: 3 },
    },
  ]
}

function mountTable(energyUnit: EnergyUnit | null, usedCounter = 180) {
  return mount(StatsMonthTable, {
    props: { months: months(usedCounter), energyUnit },
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
    const wrapper = mountTable('litres', 4250)

    expect(cells(wrapper, 'thead tr')[3]).toBe('Fuel (L)')
    expect(cells(wrapper, 'tbody tr:first-child')[3]).toBe('42.5')
    expect(cells(wrapper, 'tbody tr:last-child')[3]).toBe('-')
    expect(cells(wrapper, 'tfoot tr')[3]).toBe('42.5')
  })

  it('shows a plug-in counter in kWh as it is', () => {
    const wrapper = mountTable('kwh')

    expect(cells(wrapper, 'thead tr')[3]).toBe('Energy used (kWh)')
    expect(cells(wrapper, 'tbody tr:first-child')[3]).toBe('4,250.0')
  })

  it('shows what each month used per 100 km, and a dash for a month with too little driving', () => {
    const wrapper = mountTable('kwh')

    expect(cells(wrapper, 'thead tr')[4]).toBe('Efficiency (kWh/100 km)')
    expect(cells(wrapper, 'tbody tr:first-child')[4]).toBe('15.0')
    expect(cells(wrapper, 'tbody tr:last-child')[4]).toBe('-')
    // 180 kWh over 1,203 km together.
    expect(cells(wrapper, 'tfoot tr')[4]).toBe('15.0')
  })

  it('shows a hybrid month as fuel consumption', () => {
    const wrapper = mountTable('litres', 4250)

    expect(cells(wrapper, 'thead tr')[4]).toBe('Efficiency (L/100 km)')
    // 42.5 L over 1,200 km.
    expect(cells(wrapper, 'tbody tr:first-child')[4]).toBe('3.5')
  })
})
