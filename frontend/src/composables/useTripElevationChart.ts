import { computed, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import type { ChartData, ChartOptions } from 'chart.js'
import type { ElevationReading } from '@/services/tripLogApi'
import { downsample } from '@/utils/downsample'
import { formatTime, numberLocale } from '@/utils/format'
import { useChartTheme } from './useChartTheme'
import { useUnits } from './useUnits'
import { MAX_SPEED_CHART_POINTS } from './useTripSpeedChart'

const LINE_COLOR = '#a16207'
const FILL_COLOR = 'rgba(161,98,7,0.14)'

/**
 * The height over the course of one trip, as a line chart for StatsChartCard, in metres or feet
 * as the distances are. Null with fewer than two readings, so the panel leaves the chart out.
 * The axis follows the heights rather than starting at zero: a drive between 2 and 9 metres
 * would otherwise be a flat line along the bottom.
 */
export function useTripElevationChart(readings: Ref<readonly ElevationReading[]>) {
  const { t } = useI18n()
  const units = useUnits()
  const chartTheme = useChartTheme()

  return computed(() => {
    if (readings.value.length < 2) return null

    const shown = downsample([...readings.value], MAX_SPEED_CHART_POINTS)
    const heights = shown.map((r) => units.value.convert('elevation', r.elevationM))
    const { text, grid } = chartTheme.value
    const axis = { grid: { color: grid }, border: { color: grid } }

    const data: ChartData<'line'> = {
      labels: shown.map((r) => formatTime(r.recordedAt)),
      datasets: [
        {
          label: `${t('tripLog.detail.elevation')} (${units.value.symbol('elevation')})`,
          data: heights.map((h) => Math.round(h)),
          borderColor: LINE_COLOR,
          backgroundColor: FILL_COLOR,
          fill: 'start',
          tension: 0.3,
          pointRadius: 0,
          pointHoverRadius: 4,
        },
      ],
    }

    const options: ChartOptions<'line'> = {
      locale: numberLocale(),
      responsive: true,
      // Its height comes from the stylesheet (tripLog.css), as the speed chart's does.
      maintainAspectRatio: false,
      animation: false,
      interaction: { mode: 'index', intersect: false },
      plugins: { legend: { display: false } },
      scales: {
        x: { ...axis, ticks: { color: text, maxRotation: 0, autoSkip: true, maxTicksLimit: 5 } },
        y: { ...axis, ticks: { color: text, precision: 0 } },
      },
    }

    return { data, options }
  })
}
