import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import { useChartTheme } from '@/composables/useChartTheme'
import { useUiSettingsStore } from '@/stores/settingsUi'

describe('useChartTheme', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
  })

  afterEach(() => {
    document.documentElement.style.removeProperty('--chart-text')
    document.documentElement.style.removeProperty('--chart-grid')
  })

  it("falls back to Chart.js's own colours when the stylesheet is not there", () => {
    const theme = useChartTheme()

    expect(theme.value).toEqual({ text: '#666', grid: 'rgb(0 0 0 / 10%)' })
  })

  it('reads the tokens, and reads them again when the theme switches', async () => {
    const settings = useUiSettingsStore()
    settings.theme = 'dark'
    await nextTick()
    document.documentElement.style.setProperty('--chart-text', '#8892a4')
    document.documentElement.style.setProperty('--chart-grid', '#2e3347')
    const theme = useChartTheme()
    expect(theme.value).toEqual({ text: '#8892a4', grid: '#2e3347' })

    // What the light rule would supply once data-theme flips.
    document.documentElement.style.setProperty('--chart-text', '#5a6a7e')
    document.documentElement.style.setProperty('--chart-grid', '#cdd5df')
    settings.theme = 'light'
    await nextTick()

    expect(theme.value).toEqual({ text: '#5a6a7e', grid: '#cdd5df' })
  })
})
