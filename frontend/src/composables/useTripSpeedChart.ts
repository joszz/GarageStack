import { computed, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import type { ChartData, ChartOptions } from 'chart.js'
import type { TripPoint } from '@/services/vehicleApi'
import { downsample } from '@/utils/downsample'
import { formatTime, numberLocale } from '@/utils/format'
import { useChartTheme } from './useChartTheme'
import { useUnits } from './useUnits'

/** More points than a panel's width has pixels for only slows the chart down. */
export const MAX_SPEED_CHART_POINTS = 200

const LINE_COLOR = '#3b82f6'
const FILL_COLOR = 'rgba(59,130,246,0.12)'

/**
 * The speed over the course of one trip, as a line chart for StatsChartCard. Null when no fix
 * carried a speed, so the panel leaves the chart out rather than drawing an empty one.
 */
export function useTripSpeedChart(points: Ref<readonly TripPoint[] | null>) {
  const { t } = useI18n()
  const units = useUnits()
  const chartTheme = useChartTheme()

  return computed(() => {
    const all = points.value ?? []
    if (!all.some((p) => p.speed !== null)) return null

    const shown = downsample([...all], MAX_SPEED_CHART_POINTS)
    const unit = units.value.symbol('speed')
    const { text, grid } = chartTheme.value
    const axis = { grid: { color: grid }, border: { color: grid } }

    const data: ChartData<'line'> = {
      labels: shown.map((p) => formatTime(p.recordedAt)),
      datasets: [
        {
          label: `${t('tripLog.detail.speed')} (${unit})`,
          data: shown.map((p) => (p.speed === null ? null : units.value.convert('speed', p.speed))),
          borderColor: LINE_COLOR,
          backgroundColor: FILL_COLOR,
          fill: true,
          tension: 0.3,
          spanGaps: true,
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
        y: { ...axis, min: 0, ticks: { color: text } },
      },
    }

    return { data, options }
  })
}
