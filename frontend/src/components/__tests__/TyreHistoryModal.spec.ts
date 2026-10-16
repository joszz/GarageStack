import { describe, it, expect, vi, beforeEach } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import type { DailyStat, TyreHistory, Vehicle } from '@/services/vehicleApi'
import { useVehicleStore } from '@/stores/vehicle'
import { useUiSettingsStore } from '@/stores/settingsUi'
import { METRIC_UNITS } from '@/utils/units'
import TyreHistoryModal from '../TyreHistoryModal.vue'
import TyreLabels from '../carDiagram/TyreLabels.vue'
import { DetailModalStub } from './stubs'

const { tyreHistory } = vi.hoisted(() => ({
  tyreHistory: vi.fn<(vin: string, timeZone: string) => Promise<TyreHistory>>(),
}))
vi.mock('@/services/vehicleApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/services/vehicleApi')>()
  return {
    ...actual,
    vehicleApi: {
      ...actual.vehicleApi,
      tyreHistory,
      tyrePressureThresholds: () => new Promise(() => {}),
    },
  }
})

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

function day(date: string, rearRight: number | null): DailyStat {
  return {
    date,
    evSocPercent: null,
    fuelLevelPercent: null,
    tyrePressureFrontLeft: 2.4,
    tyrePressureFrontRight: 2.4,
    tyrePressureRearLeft: 2.5,
    tyrePressureRearRight: rearRight,
    batteryVoltage: null,
    counterTotal: null,
  }
}

async function render(history: TyreHistory) {
  tyreHistory.mockResolvedValue(history)
  const wrapper = mount(TyreHistoryModal, {
    props: { open: false },
    global: {
      plugins: [i18n],
      stubs: { DetailModal: DetailModalStub, StatsChartCard: true, FontAwesomeIcon: true },
    },
  })
  await wrapper.setProps({ open: true })
  await flushPromises()
  return wrapper
}

describe('TyreHistoryModal', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    useUiSettingsStore().units = { ...METRIC_UNITS }
    useVehicleStore().vehicles = [{ vin: 'FAKEVN00000000001' } as Vehicle]
    tyreHistory.mockReset()
  })

  it('marks the tyre falling behind the others as losing air', async () => {
    const wrapper = await render({
      days: [day('2026-10-15', 2.3), day('2026-10-16', 2.26)],
      trend: { frontLeft: 0.06, frontRight: 0.05, rearLeft: 0.07, rearRight: -0.2, days: 14 },
      trendDays: 14,
    })

    expect(tyreHistory).toHaveBeenCalledWith('FAKEVN00000000001', expect.any(String))
    const rows = wrapper.findAll('.detail-list__item')
    expect(rows).toHaveLength(4)
    const rearRight = rows[3]!
    expect(rearRight.classes()).toContain('detail-list__item--alert')
    expect(rearRight.text()).toContain('−0.20')
    expect(rearRight.text()).toContain(en.tyreHistory.losing)
    expect(rows[0]!.classes()).not.toContain('detail-list__item--alert')
    expect(rows[0]!.text()).toContain('+0.06')
    expect(wrapper.find('stats-chart-card-stub').exists()).toBe(true)
  })

  it('says a trend needs more days rather than showing an empty list', async () => {
    const wrapper = await render({ days: [day('2026-10-16', 2.5)], trend: null, trendDays: 14 })

    expect(wrapper.text()).toContain(en.tyreHistory.noTrend)
    expect(wrapper.findAll('.detail-list__item')).toHaveLength(0)
  })

  it('says so when no tyre reported in the month', async () => {
    const empty = { ...day('2026-10-16', null), tyrePressureFrontLeft: null }
    const wrapper = await render({
      days: [{ ...empty, tyrePressureFrontRight: null, tyrePressureRearLeft: null }],
      trend: null,
      trendDays: 14,
    })

    expect(wrapper.text()).toContain(en.tyreHistory.noData)
    expect(wrapper.find('stats-chart-card-stub').exists()).toBe(false)
  })
})

describe('TyreLabels', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    useUiSettingsStore().units = { ...METRIC_UNITS }
  })

  it('opens the history from any tyre, named for screen readers', async () => {
    const wrapper = mount(TyreLabels, {
      props: { pressures: { frontLeft: 2.4, frontRight: 2.4, rearLeft: 2.5, rearRight: 2.26 } },
      global: { plugins: [i18n] },
    })

    const rearRight = wrapper.findAll('button')[3]!
    expect(rearRight.attributes('aria-label')).toContain('Rear right')
    await rearRight.trigger('click')

    expect(wrapper.emitted('select')).toEqual([['rearRight']])
  })
})
