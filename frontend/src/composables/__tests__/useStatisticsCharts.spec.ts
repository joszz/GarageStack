import { describe, it, expect, beforeEach } from 'vitest'
import { defineComponent, h, ref, type Ref } from 'vue'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import type { VehicleType } from '@/stores/vehicle'
import { useStatisticsCharts } from '@/composables/useStatisticsCharts'

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

function chartsFor(type: VehicleType) {
  const result: { charts: ReturnType<typeof useStatisticsCharts> | null } = { charts: null }
  mount(
    defineComponent({
      setup() {
        result.charts = useStatisticsCharts(ref([]) as Ref<never[]>, ref('day'), ref(type))
        return () => h('div')
      },
    }),
    { global: { plugins: [i18n] } },
  )
  return result.charts!.value
}

function dailyEnergyChart(type: VehicleType) {
  return chartsFor(type).find((chart) => chart.id === 'dailyKwhChart')!
}

describe('useStatisticsCharts daily energy chart', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('is offered to a BEV, in kWh', () => {
    const chart = dailyEnergyChart('bev')

    expect(chart.vehicleApplicable).toBe(true)
    expect(chart.title).toBe(en.statistics.energyChart.day)
  })

  it('is offered to a plug-in hybrid, in kWh', () => {
    const chart = dailyEnergyChart('phev')

    expect(chart.vehicleApplicable).toBe(true)
    expect(chart.title).toBe(en.statistics.energyChart.day)
  })

  it('is offered to a plain hybrid, as fuel', () => {
    const chart = dailyEnergyChart('hev')

    expect(chart.vehicleApplicable).toBe(true)
    expect(chart.icon).toBe('gas-pump')
  })

  it('waits for the drivetrain to be known', () => {
    expect(dailyEnergyChart('unknown').vehicleApplicable).toBe(false)
  })
})
