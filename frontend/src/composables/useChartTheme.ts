import { computed } from 'vue'
import { useUiSettingsStore } from '@/stores/settingsUi'

/** The colours Chart.js draws a chart's axis text, legend and grid lines in. */
export interface ChartTheme {
  text: string
  grid: string
}

// Chart.js's own defaults, for a document without the stylesheet (unit tests).
const FALLBACK: ChartTheme = { text: '#666', grid: 'rgb(0 0 0 / 10%)' }

/**
 * The chart colours of the current theme, from the --chart-* tokens in main.css. A canvas cannot
 * use CSS variables, so they are read off the document, and read again on every theme switch
 * (the settings store sets `data-theme` before the next render, which is when this is read).
 */
export function useChartTheme() {
  const settings = useUiSettingsStore()
  return computed<ChartTheme>(() => {
    // Read for the dependency: the values come from the stylesheet, not from the setting.
    void settings.theme
    const style = getComputedStyle(document.documentElement)
    const token = (name: string, fallback: string) =>
      style.getPropertyValue(name).trim() || fallback
    return {
      text: token('--chart-text', FALLBACK.text),
      grid: token('--chart-grid', FALLBACK.grid),
    }
  })
}
