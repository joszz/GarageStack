import { computed, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import type { ChartData, ChartOptions } from 'chart.js'
import type { StatsChartType } from '@/components/StatsChartCard.vue'
import type { TelemetryHistoryPoint } from '@/services/vehicleApi'
import type { StatsChartId } from '@/stores/settingsShared'
import { useUiSettingsStore } from '@/stores/settingsUi'
import type { VehicleType } from '@/stores/vehicle'
import { energyUnit, litres } from '@/utils/energy'
import { formatDate, intlLocale } from '@/utils/format'
import {
  dailyAverages,
  dailyCounterTotals,
  roundedRange,
  roundTo2,
  type HistoryDay,
} from '@/utils/statistics'
import { burnsFuel, mayPlugIn } from '@/utils/vehicleType'
import { useUnits } from './useUnits'

export interface ChartDef {
  id: StatsChartId
  icon: string
  title: string
  description: string
  /** Whether this chart means anything for the drivetrain at all. */
  vehicleApplicable: boolean
  /** Whether there is something to draw right now. */
  applicable: boolean
  type: StatsChartType
  data: ChartData<StatsChartType>
  options: ChartOptions<StatsChartType>
}

const CHART_ICONS: Record<StatsChartId, string> = {
  evChart: 'bolt',
  tyreChart: 'gauge',
  hybridSocChart: 'wave-square',
  dailyKwhChart: 'bolt-lightning',
}

const TYRE_SERIES: {
  label: string
  read: (p: TelemetryHistoryPoint) => number | null
  color: string
}[] = [
  { label: 'FL', read: (p) => p.tyrePressureFrontLeft, color: '#f59e0b' },
  { label: 'FR', read: (p) => p.tyrePressureFrontRight, color: '#ef4444' },
  { label: 'RL', read: (p) => p.tyrePressureRearLeft, color: '#8b5cf6' },
  { label: 'RR', read: (p) => p.tyrePressureRearRight, color: '#ec4899' },
]

// Every line series shares the same shape and styling; only label, data and colour differ.
function lineDataset(label: string, data: (number | null)[], color: string, fillColor?: string) {
  return {
    label,
    data,
    borderColor: color,
    backgroundColor: fillColor ?? 'transparent',
    fill: fillColor !== undefined,
    tension: 0.3,
    spanGaps: true,
    pointRadius: 2,
    pointHoverRadius: 4,
  }
}

/**
 * The statistics view's charts, one per day of the period: the traction battery, the tyres, a
 * hybrid's battery against its tank, and the daily energy (or fuel) used. Each carries whether it
 * applies to this drivetrain and whether there is anything to draw yet.
 */
export function useStatisticsCharts(
  days: Ref<HistoryDay[]>,
  vehicleType: Ref<VehicleType>,
): Ref<ChartDef[]> {
  const { t } = useI18n()
  const units = useUnits()
  const uiSettings = useUiSettingsStore()

  const hasLargeEv = computed(() => mayPlugIn(vehicleType.value))
  const isHybrid = computed(() => burnsFuel(vehicleType.value))
  const hasData = computed(() => days.value.some((day) => day.points.length > 0))

  const labels = computed(() => days.value.map((d) => formatDate(new Date(`${d.key}T00:00:00`))))

  const evChartData = computed(() => ({
    labels: labels.value,
    datasets: [
      lineDataset(
        `${t('vehicle.evSoc')} (%)`,
        dailyAverages(days.value, (p) => p.evSocPercent),
        '#10b981',
        'rgba(16,185,129,0.1)',
      ),
    ],
  }))

  const tyreChartData = computed(() => {
    const u = units.value
    return {
      labels: labels.value,
      datasets: TYRE_SERIES.map(({ label, read, color }) =>
        lineDataset(
          `${label} (${u.symbol('pressure')})`,
          dailyAverages(days.value, read).map((bar) =>
            bar === null ? null : u.convert('pressure', bar),
          ),
          color,
        ),
      ),
    }
  })

  const hybridSocChartData = computed(() => ({
    labels: labels.value,
    datasets: [
      lineDataset(
        `${t('vehicle.evSoc')} (%)`,
        dailyAverages(days.value, (p) => p.evSocPercent),
        '#10b981',
      ),
      lineDataset(
        `${t('vehicle.fuel')} (%)`,
        dailyAverages(days.value, (p) => p.fuelLevelPercent),
        '#3b82f6',
      ),
    ],
  }))

  // The same counter, read in the unit this drivetrain actually reports: kWh out of the traction
  // battery on a plug-in car, litres of fuel on a plain hybrid. See utils/energy.
  const reportsFuelCounter = computed(() => energyUnit(vehicleType.value) === 'litres')

  const dailyEnergyChartData = computed(() => ({
    labels: labels.value,
    datasets: [
      {
        label: reportsFuelCounter.value ? units.value.symbol('volume') : t('common.kwh'),
        data: dailyCounterTotals(days.value).map((total) => {
          const fuel = reportsFuelCounter.value ? litres(total) : null
          const used = fuel !== null ? units.value.convert('volume', fuel) : total
          return used !== null ? roundTo2(used) : null
        }),
        borderColor: '#f59e0b',
        backgroundColor: 'rgba(245,158,11,0.7)',
      },
    ],
  }))

  // All charts share the same responsive/axis setup; they differ only in aspect ratio, legend
  // and y-axis range. The locale makes axis and tooltip numbers read like the rest of the page.
  function chartOptions(aspectRatio: number, legend: boolean, y: { min: number; max?: number }) {
    return {
      locale: intlLocale(uiSettings.locale),
      responsive: true,
      maintainAspectRatio: true,
      aspectRatio,
      animation: false as const,
      plugins: { legend: { display: legend } },
      scales: {
        x: { ticks: { maxRotation: 0, autoSkip: true, maxTicksLimit: 8 } },
        y,
      },
    }
  }

  const percentOptions = computed(() => chartOptions(2.6, false, { min: 0, max: 100 }))
  const pressureOptions = computed(() => {
    const u = units.value
    return chartOptions(
      2.3,
      true,
      roundedRange(u.convert('pressure', 1.5), u.convert('pressure', 3.5)),
    )
  })
  const hybridSocOptions = computed(() => chartOptions(2.6, true, { min: 0, max: 100 }))
  const kwhOptions = computed(() => chartOptions(2.6, false, { min: 0 }))

  return computed((): ChartDef[] => [
    {
      id: 'evChart',
      icon: CHART_ICONS.evChart,
      title: t('vehicle.evSoc'),
      description: t('statistics.chartDesc.evChart'),
      vehicleApplicable: hasLargeEv.value,
      applicable: hasLargeEv.value && hasData.value,
      type: 'line',
      data: evChartData.value,
      options: percentOptions.value,
    },
    {
      id: 'tyreChart',
      icon: CHART_ICONS.tyreChart,
      title: t('vehicle.tyres'),
      description: t('statistics.chartDesc.tyreChart', {
        low: units.value.measure('pressure', 2)!.value,
        high: units.value.measure('pressure', 3)!.value,
        unit: units.value.symbol('pressure'),
      }),
      vehicleApplicable: true,
      applicable: hasData.value,
      type: 'line',
      data: tyreChartData.value,
      options: pressureOptions.value,
    },
    {
      id: 'hybridSocChart',
      icon: CHART_ICONS.hybridSocChart,
      title: t('statistics.hybridSocChart'),
      description: t('statistics.chartDesc.hybridSocChart'),
      vehicleApplicable: isHybrid.value,
      applicable: isHybrid.value && hasData.value,
      type: 'line',
      data: hybridSocChartData.value,
      options: hybridSocOptions.value,
    },
    {
      id: 'dailyKwhChart',
      icon: reportsFuelCounter.value ? 'gas-pump' : CHART_ICONS.dailyKwhChart,
      title: reportsFuelCounter.value
        ? t('statistics.dailyFuelChart', { unit: units.value.symbol('volume') })
        : t('statistics.dailyKwhChart'),
      description: reportsFuelCounter.value
        ? t('statistics.chartDesc.dailyFuelChart')
        : t('statistics.chartDesc.dailyKwhChart'),
      vehicleApplicable: isHybrid.value,
      applicable: isHybrid.value && hasData.value,
      type: 'bar',
      data: dailyEnergyChartData.value,
      options: kwhOptions.value,
    },
  ])
}
