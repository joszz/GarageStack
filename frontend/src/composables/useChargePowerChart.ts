import { computed, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import type { ChartData, ChartOptions } from 'chart.js'
import type { ChargeSessionPowerPoint } from '@/services/vehicleApi'
import { downsample } from '@/utils/downsample'
import { formatTime, numberLocale } from '@/utils/format'
import { useChartTheme } from './useChartTheme'

/** More points than a panel's width has pixels for only slows the chart down. */
export const MAX_POWER_CHART_POINTS = 200

const LINE_COLOR = '#22c55e'
const FILL_COLOR = 'rgba(34,197,94,0.12)'

/**
 * One charge session's power over time, as a line chart for StatsChartCard. Null until the curve
 * has loaded, or when the car reported no power during the session.
 */
export function useChargePowerChart(points: Ref<readonly ChargeSessionPowerPoint[] | null>) {
  const { t } = useI18n()
  const chartTheme = useChartTheme()

  return computed(() => {
    const all = points.value ?? []
    if (all.length === 0) return null

    const shown = downsample([...all], MAX_POWER_CHART_POINTS)
    const { text, grid } = chartTheme.value
    const axis = { grid: { color: grid }, border: { color: grid } }

    const data: ChartData<'line'> = {
      labels: shown.map((p) => formatTime(p.recordedAt)),
      datasets: [
        {
          label: `${t('chargeHistory.power')} (kW)`,
          data: shown.map((p) => p.powerKw),
          borderColor: LINE_COLOR,
          backgroundColor: FILL_COLOR,
          fill: true,
          tension: 0.3,
          pointRadius: 0,
          pointHoverRadius: 4,
        },
      ],
    }

    const options: ChartOptions<'line'> = {
      locale: numberLocale(),
      responsive: true,
      maintainAspectRatio: true,
      aspectRatio: 2.4,
      animation: false,
      interaction: { mode: 'index', intersect: false },
      plugins: { legend: { display: false } },
      scales: {
        x: { ...axis, ticks: { color: text, maxRotation: 0, autoSkip: true, maxTicksLimit: 5 } },
        y: {
          ...axis,
          min: 0,
          ticks: { color: text },
          title: { display: true, text: 'kW', color: text },
        },
      },
    }

    return { data, options }
  })
}
