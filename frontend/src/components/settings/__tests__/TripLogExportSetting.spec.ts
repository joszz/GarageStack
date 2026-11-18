import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { enableAutoUnmount, flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia, type Pinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import { tripLogApi } from '@/services/tripLogApi'
import type { Vehicle } from '@/services/vehicleApi'
import { useVehicleStore } from '@/stores/vehicle'
import { useUiSettingsStore } from '@/stores/settingsUi'
import { downloadCsv } from '@/utils/download'
import { deventer, logEntry, zwolle } from '@/services/__tests__/tripLogFixtures'
import TripLogExportSetting from '../TripLogExportSetting.vue'

vi.mock('@/services/tripLogApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/services/tripLogApi')>()
  return {
    ...actual,
    tripLogApi: {
      log: vi.fn<typeof actual.tripLogApi.log>(),
      resolvePlaces: vi.fn<typeof actual.tripLogApi.resolvePlaces>(),
    },
  }
})

vi.mock('@/utils/download', () => ({ downloadCsv: vi.fn<typeof downloadCsv>() }))

enableAutoUnmount(afterEach)

const car = { id: 1, vin: 'FAKEVN00000000001', vehicleType: 'hev' } as Vehicle

let pinia: Pinia

function render() {
  const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })
  return mount(TripLogExportSetting, {
    global: { plugins: [pinia, i18n], stubs: { FontAwesomeIcon: true } },
  })
}

function downloadButton(wrapper: ReturnType<typeof render>) {
  return wrapper.get('button')
}

function savedCsv(): string {
  return vi.mocked(downloadCsv).mock.calls[0]![1]
}

describe('TripLogExportSetting', () => {
  beforeEach(() => {
    pinia = createPinia()
    setActivePinia(pinia)
    vi.clearAllMocks()
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(new Date('2026-10-19T12:00:00Z'))
    useVehicleStore(pinia).vehicles = [car]
    vi.mocked(tripLogApi.log).mockResolvedValue([
      logEntry({ id: 1, startPlace: zwolle, endPlace: deventer, notes: 'Client visit' }),
      logEntry({ id: 2, isHidden: true, notes: 'GPS glitch' }),
    ])
    vi.mocked(tripLogApi.resolvePlaces).mockResolvedValue({
      trips: [],
      available: true,
      hasMore: false,
    })
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('saves this month by default, hidden trips left out', async () => {
    const wrapper = render()

    await downloadButton(wrapper).trigger('click')
    await flushPromises()

    const [vin, from, to] = vi.mocked(tripLogApi.log).mock.lastCall!
    expect(vin).toBe(car.vin)
    expect(new Date(from)).toEqual(new Date(2026, 9, 1))
    expect(new Date(to)).toEqual(new Date(2026, 10, 1))
    expect(vi.mocked(downloadCsv).mock.calls[0]![0]).toBe('trip-log-2026-10.csv')
    expect(savedCsv()).toContain('Client visit')
    expect(savedCsv()).not.toContain('GPS glitch')
    expect(tripLogApi.resolvePlaces).not.toHaveBeenCalled()
  })

  it('saves the whole year once the month is cleared', async () => {
    const wrapper = render()
    const month = wrapper.findAll('select')[1]!
    ;(month.element as HTMLSelectElement).selectedIndex = 0
    await month.trigger('change')

    await downloadButton(wrapper).trigger('click')
    await flushPromises()

    expect(vi.mocked(downloadCsv).mock.calls[0]![0]).toBe('trip-log-2026.csv')
  })

  it('looks up the addresses still missing before it saves', async () => {
    vi.mocked(tripLogApi.log).mockResolvedValue([logEntry({ id: 1 })])
    let answer: () => void = () => {}
    vi.mocked(tripLogApi.resolvePlaces).mockReturnValue(
      new Promise((resolve) => {
        answer = () =>
          resolve({
            trips: [{ id: 1, startPlace: zwolle, endPlace: deventer }],
            available: true,
            hasMore: false,
          })
      }),
    )
    const wrapper = render()

    await downloadButton(wrapper).trigger('click')
    await flushPromises()

    expect(wrapper.get('[role=status]').text()).toBe('Looking up addresses: 1 trip to go')
    expect(downloadButton(wrapper).attributes('disabled')).toBeDefined()
    expect(downloadCsv).not.toHaveBeenCalled()

    answer()
    await flushPromises()

    expect(tripLogApi.resolvePlaces).toHaveBeenCalledWith(car.vin, [1], 'en')
    expect(savedCsv()).toContain('Grote Markt 1')
    expect(wrapper.find('[role=status]').exists()).toBe(false)
    expect(downloadButton(wrapper).attributes('disabled')).toBeUndefined()
  })

  it('writes coordinates and looks nothing up while place names are off', async () => {
    useUiSettingsStore(pinia).placeNamesEnabled = false
    const wrapper = render()

    await downloadButton(wrapper).trigger('click')
    await flushPromises()

    expect(tripLogApi.resolvePlaces).not.toHaveBeenCalled()
    expect(savedCsv()).not.toContain('Grote Markt')
    expect(savedCsv()).toContain('52.5123')
  })

  it('saves nothing once the settings close during the lookup', async () => {
    vi.mocked(tripLogApi.log).mockResolvedValue([logEntry({ id: 1 })])
    let answer: () => void = () => {}
    vi.mocked(tripLogApi.resolvePlaces).mockReturnValue(
      new Promise((resolve) => {
        answer = () => resolve({ trips: [], available: true, hasMore: true })
      }),
    )
    const wrapper = render()
    await downloadButton(wrapper).trigger('click')
    await flushPromises()

    wrapper.unmount()
    answer()
    await flushPromises()

    expect(tripLogApi.resolvePlaces).toHaveBeenCalledOnce()
    expect(downloadCsv).not.toHaveBeenCalled()
  })

  it('says so when the period has no trips to save', async () => {
    vi.mocked(tripLogApi.log).mockResolvedValue([logEntry({ id: 2, isHidden: true })])
    const wrapper = render()

    await downloadButton(wrapper).trigger('click')
    await flushPromises()

    expect(downloadCsv).not.toHaveBeenCalled()
    expect(wrapper.get('[role=status]').text()).toBe(en.settings.tripLogExport.empty)
  })

  it('says why the trips could not be read', async () => {
    vi.mocked(tripLogApi.log).mockRejectedValue(new Error('Network error'))
    const wrapper = render()

    await downloadButton(wrapper).trigger('click')
    await flushPromises()

    expect(downloadCsv).not.toHaveBeenCalled()
    expect(wrapper.find('[role=alert]').exists()).toBe(true)
  })
})
