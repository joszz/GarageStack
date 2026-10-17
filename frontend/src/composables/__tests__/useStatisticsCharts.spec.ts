import { describe, it, expect, beforeEach } from 'vitest'
import { defineComponent, h, ref } from 'vue'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import nl from '@/locales/nl.json'
import type { DailyStat } from '@/services/vehicleApi'
import type { VehicleType } from '@/stores/vehicle'
import { useUiSettingsStore } from '@/stores/settingsUi'
import type { StatsBucket } from '@/utils/statistics'
import {
  useLevelChart,
  useStatisticsCharts,
  type LevelKind,
} from '@/composables/useStatisticsCharts'

function day(date: string, values: Partial<DailyStat> = {}): DailyStat {
  return {
    date,
    evSocPercent: null,
    fuelLevelPercent: null,
    tyrePressureFrontLeft: null,
    tyrePressureFrontRight: null,
    tyrePressureRearLeft: null,
    tyrePressureRearRight: null,
    batteryVoltage: null,
    counterTotal: null,
    ...values,
  }
}

function bucket(key: string, days: DailyStat[]): StatsBucket {
  return { key, start: new Date(`${key}T00:00:00`), days }
}

function chartsFor(type: VehicleType, buckets: StatsBucket[] = [], locale: 'en' | 'nl' = 'en') {
  const i18n = createI18n({ legacy: false, locale, messages: { en, nl } })
  const result: { charts: ReturnType<typeof useStatisticsCharts> | null } = { charts: null }
  mount(
    defineComponent({
      setup() {
        result.charts = useStatisticsCharts(ref(buckets), ref('day'), ref(type))
        return () => h('div')
      },
    }),
    { global: { plugins: [i18n] } },
  )
  return result.charts!.value
}

function chart(
  type: VehicleType,
  id: string,
  buckets: StatsBucket[] = [],
  locale: 'en' | 'nl' = 'en',
) {
  return chartsFor(type, buckets, locale).find((c) => c.id === id)!
}

const TWO_DAYS = [
  bucket('2026-10-14', [
    day('2026-10-14', { evSocPercent: 60, tyrePressureFrontLeft: 2.4, counterTotal: 5 }),
  ]),
  bucket('2026-10-15', [
    day('2026-10-15', { evSocPercent: 40, tyrePressureFrontLeft: 2.2, counterTotal: 7 }),
  ]),
]

describe('useStatisticsCharts', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    useUiSettingsStore().units = { ...useUiSettingsStore().units, pressure: 'bar' }
  })

  describe('daily energy chart', () => {
    it('is offered to a BEV, in kWh', () => {
      const c = chart('bev', 'dailyKwhChart')

      expect(c.vehicleApplicable).toBe(true)
      expect(c.title).toBe(en.statistics.energyChart.day)
    })

    it('is offered to a plug-in hybrid, in kWh', () => {
      const c = chart('phev', 'dailyKwhChart')

      expect(c.vehicleApplicable).toBe(true)
      expect(c.title).toBe(en.statistics.energyChart.day)
    })

    it('is offered to a plain hybrid, as fuel', () => {
      const c = chart('hev', 'dailyKwhChart')

      expect(c.vehicleApplicable).toBe(true)
      expect(c.icon).toBe('gas-pump')
    })

    it('waits for the drivetrain to be known', () => {
      expect(chart('unknown', 'dailyKwhChart').vehicleApplicable).toBe(false)
    })

    it('adds up each point', () => {
      expect(chart('bev', 'dailyKwhChart', TWO_DAYS).data.datasets[0]!.data).toEqual([5, 7])
    })
  })

  it('offers each drivetrain only the charts that mean something for it', () => {
    const offered = (type: VehicleType) =>
      chartsFor(type)
        .filter((c) => c.vehicleApplicable)
        .map((c) => c.id)

    expect(offered('bev')).toEqual(['evChart', 'tyreChart', 'dailyKwhChart'])
    expect(offered('phev')).toEqual(['evChart', 'tyreChart', 'hybridSocChart', 'dailyKwhChart'])
    expect(offered('hev')).toEqual(['tyreChart', 'hybridSocChart', 'dailyKwhChart'])
  })

  // A canvas draws in whatever colours it is given; left alone, Chart.js greys suit neither theme.
  it("draws axes, grid and legend in the theme's chart colours", () => {
    document.documentElement.style.setProperty('--chart-text', '#5a6a7e')
    document.documentElement.style.setProperty('--chart-grid', '#cdd5df')
    const options = chart('bev', 'tyreChart', TWO_DAYS).options as {
      plugins: { legend: { labels: { color: string } } }
      scales: Record<'x' | 'y', { ticks: { color: string }; grid: { color: string } }>
    }
    document.documentElement.style.removeProperty('--chart-text')
    document.documentElement.style.removeProperty('--chart-grid')

    expect(options.plugins.legend.labels.color).toBe('#5a6a7e')
    expect(options.scales.x.ticks.color).toBe('#5a6a7e')
    expect(options.scales.y.ticks.color).toBe('#5a6a7e')
    expect(options.scales.x.grid.color).toBe('#cdd5df')
    expect(options.scales.y.grid.color).toBe('#cdd5df')
  })

  it('has nothing to draw until there is data', () => {
    expect(chart('bev', 'evChart').applicable).toBe(false)
    expect(chart('bev', 'evChart', TWO_DAYS).applicable).toBe(true)
  })

  it('draws the battery level per point', () => {
    expect(chart('bev', 'evChart', TWO_DAYS).data.datasets[0]!.data).toEqual([60, 40])
  })

  it('names the tyres in the interface language', () => {
    const labels = (locale: 'en' | 'nl') =>
      chart('bev', 'tyreChart', TWO_DAYS, locale).data.datasets.map((d) => d.label)

    expect(labels('en')).toEqual(['FL (bar)', 'FR (bar)', 'RL (bar)', 'RR (bar)'])
    expect(labels('nl')).toEqual(['LV (bar)', 'RV (bar)', 'LA (bar)', 'RA (bar)'])
  })

  it('shows tyre pressure in the chosen unit', () => {
    useUiSettingsStore().units = { ...useUiSettingsStore().units, pressure: 'psi' }

    const frontLeft = chart('bev', 'tyreChart', TWO_DAYS).data.datasets[0]!

    expect(frontLeft.label).toBe('FL (psi)')
    expect((frontLeft.data as number[]).map((v) => Math.round(v))).toEqual([35, 32])
  })
})

describe('useLevelChart', () => {
  function levelChart(level: LevelKind, buckets: StatsBucket[]) {
    const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })
    const result: { chart: ReturnType<typeof useLevelChart> | null } = { chart: null }
    mount(
      defineComponent({
        setup() {
          result.chart = useLevelChart(ref(buckets), ref('day'), ref(level))
          return () => h('div')
        },
      }),
      { global: { plugins: [i18n] } },
    )
    return result.chart!.value
  }

  beforeEach(() => setActivePinia(createPinia()))

  it('draws the tank on its own, per point', () => {
    const c = levelChart('fuel', [
      bucket('2026-10-14', [day('2026-10-14', { fuelLevelPercent: 70, evSocPercent: 50 })]),
      bucket('2026-10-15', [day('2026-10-15', { fuelLevelPercent: 64, evSocPercent: 55 })]),
    ])

    expect(c.title).toBe(en.vehicle.fuel)
    expect(c.data.datasets).toHaveLength(1)
    expect(c.data.datasets[0]!.data).toEqual([70, 64])
    expect(c.hasReadings).toBe(true)
  })

  it('has nothing to draw when the car reported none of the level', () => {
    expect(levelChart('fuel', TWO_DAYS).hasReadings).toBe(false)
    expect(levelChart('battery', TWO_DAYS).hasReadings).toBe(true)
  })
})
