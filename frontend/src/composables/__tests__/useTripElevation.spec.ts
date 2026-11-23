import { describe, it, expect, vi, beforeEach } from 'vitest'
import { defineComponent, h, nextTick, ref } from 'vue'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import { tripLogApi, type ElevationReading, type TripLogEntry } from '@/services/tripLogApi'
import { logEntry } from '@/services/__tests__/tripLogFixtures'
import { useUiSettingsStore } from '@/stores/settingsUi'
import { useTripElevation } from '../useTripElevation'
import { useTripElevationChart } from '../useTripElevationChart'

vi.mock('@/services/tripLogApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/services/tripLogApi')>()
  return { ...actual, tripLogApi: { elevation: vi.fn<typeof actual.tripLogApi.elevation>() } }
})

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

const reading = (minute: number, elevationM: number): ElevationReading => ({
  recordedAt: `2026-09-24T12:${String(minute).padStart(2, '0')}:00.000Z`,
  elevationM,
})

function mountComposables(initial: TripLogEntry | null) {
  const entry = ref<TripLogEntry | null>(initial)
  let readings!: ReturnType<typeof useTripElevation>
  let chart!: ReturnType<typeof useTripElevationChart>
  mount(
    defineComponent({
      setup() {
        readings = useTripElevation(ref('FAKEVN00000000001'), entry)
        chart = useTripElevationChart(readings)
        return () => h('div')
      },
    }),
    { global: { plugins: [i18n] } },
  )
  return { entry, readings, chart }
}

describe('useTripElevation', () => {
  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('loads the picked trip once, and again only when its span changed', async () => {
    vi.mocked(tripLogApi.elevation).mockResolvedValue([reading(32, 2), reading(40, 9)])
    const { entry, readings } = mountComposables(logEntry())
    await flushPromises()
    expect(readings.value).toHaveLength(2)

    entry.value = logEntry({ id: 2 })
    await flushPromises()
    entry.value = logEntry()
    await flushPromises()
    expect(tripLogApi.elevation).toHaveBeenCalledTimes(2)

    entry.value = logEntry({ endedAt: '2026-09-24T13:20:00.000Z' })
    await flushPromises()
    expect(tripLogApi.elevation).toHaveBeenCalledTimes(3)
  })

  it('shows no profile when it cannot be had', async () => {
    vi.mocked(tripLogApi.elevation).mockRejectedValue(new Error('offline'))
    const { readings, chart } = mountComposables(logEntry())
    await flushPromises()

    expect(readings.value).toEqual([])
    expect(chart.value).toBeNull()
  })

  it('draws the heights in the unit the distances are in', async () => {
    vi.mocked(tripLogApi.elevation).mockResolvedValue([reading(32, 3.2), reading(40, 30.48)])
    useUiSettingsStore().units = { ...useUiSettingsStore().units, distance: 'mi' }
    const { chart } = mountComposables(logEntry())
    await flushPromises()
    await nextTick()

    expect(chart.value!.data.datasets[0]!.label).toBe('Elevation (ft)')
    expect(chart.value!.data.datasets[0]!.data).toEqual([10, 100])
  })
})
