import { describe, it, expect, vi, beforeEach } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import type { DailyStat, TelemetrySnapshot, Vehicle } from '@/services/vehicleApi'
import type { LevelKind } from '@/composables/useStatisticsCharts'
import { useVehicleStore, type VehicleType } from '@/stores/vehicle'
import { useUiSettingsStore } from '@/stores/settingsUi'
import { METRIC_UNITS } from '@/utils/units'
import LevelHistoryModal from '../LevelHistoryModal.vue'
import LevelBadge from '../carDiagram/LevelBadge.vue'
import { DetailModalStub } from './stubs'

const { dailyStats } = vi.hoisted(() => ({
  dailyStats:
    vi.fn<(vin: string, from: string, to: string, timeZone: string) => Promise<DailyStat[]>>(),
}))
vi.mock('@/services/vehicleApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/services/vehicleApi')>()
  return { ...actual, vehicleApi: { ...actual.vehicleApi, dailyStats } }
})

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

// Names each chart it is handed, so a spec can tell the level's from the usage one.
const StatsChartCardStub = {
  name: 'StatsChartCard',
  props: ['title', 'icon', 'type', 'data', 'options', 'showInfo'],
  template: '<div class="chart-stub">{{ title }}</div>',
}

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

// The fields the level details read, all empty unless a spec fills them in.
function status(values: Partial<TelemetrySnapshot> = {}): TelemetrySnapshot {
  return {
    fuelLevelPercent: null,
    fuelRangeKm: null,
    evSocPercent: null,
    electricRangeKm: null,
    isCharging: null,
    chargerConnected: null,
    remainingChargingTime: null,
    socTargetPercent: null,
    chargingLastEndAt: null,
    lastChargeEndingPower: null,
    ...values,
  } as TelemetrySnapshot
}

const TWO_DAYS = [
  day('2026-10-15', { fuelLevelPercent: 60, evSocPercent: 55, counterTotal: 420 }),
  day('2026-10-16', { fuelLevelPercent: 52, evSocPercent: 58, counterTotal: 380 }),
]

async function render(
  level: LevelKind,
  type: VehicleType,
  days: DailyStat[] = TWO_DAYS,
  snapshot: TelemetrySnapshot = status(),
) {
  useUiSettingsStore().vehicleTypeOverride = type
  useVehicleStore().currentStatus = snapshot
  dailyStats.mockResolvedValue(days)
  const wrapper = mount(LevelHistoryModal, {
    props: { open: false, level },
    global: {
      plugins: [i18n],
      stubs: {
        DetailModal: DetailModalStub,
        StatsChartCard: StatsChartCardStub,
        FontAwesomeIcon: true,
      },
    },
  })
  await wrapper.setProps({ open: true })
  await flushPromises()
  return wrapper
}

const charts = (wrapper: Awaited<ReturnType<typeof render>>) =>
  wrapper.findAll('.chart-stub').map((c) => c.text())

describe('LevelHistoryModal', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    useUiSettingsStore().units = { ...METRIC_UNITS }
    useVehicleStore().vehicles = [{ vin: 'FAKEVN00000000001' } as Vehicle]
    dailyStats.mockReset()
  })

  it('asks for the last 30 days, today included', async () => {
    await render('fuel', 'hev')

    const [vin, from, to] = dailyStats.mock.lastCall!
    expect(vin).toBe('FAKEVN00000000001')
    const span = (new Date(to).getTime() - new Date(from).getTime()) / 86_400_000
    expect(Math.round(span)).toBe(29)
  })

  // A plain hybrid's daily counter holds fuel, so its usage belongs with the tank.
  it("charts a hybrid's tank next to the fuel it burned per day", async () => {
    const wrapper = await render('fuel', 'hev')

    expect(charts(wrapper)).toEqual([en.vehicle.fuel, 'Fuel Use per Day (L)'])
  })

  it("leaves the fuel out of a hybrid's battery history", async () => {
    const wrapper = await render('battery', 'hev')

    expect(charts(wrapper)).toEqual([en.vehicle.evSoc])
  })

  // On a car that plugs in, the same counter holds kWh out of the battery.
  it("charts a plug-in car's battery next to the energy it used per day", async () => {
    const wrapper = await render('battery', 'bev')

    expect(charts(wrapper)).toEqual([en.vehicle.evSoc, en.statistics.energyChart.day])
  })

  it("leaves the kWh out of a plug-in hybrid's fuel history", async () => {
    const wrapper = await render('fuel', 'phev')

    expect(charts(wrapper)).toEqual([en.vehicle.fuel])
  })

  it('says so when the level was not reported in the month', async () => {
    const wrapper = await render('fuel', 'hev', [day('2026-10-16', { evSocPercent: 50 })])

    expect(charts(wrapper)).toEqual([])
    expect(wrapper.text()).toContain('No fuel level reported in the last 30 days.')
  })

  it('shows the level and the range it is good for now', async () => {
    const wrapper = await render(
      'fuel',
      'hev',
      TWO_DAYS,
      status({ fuelLevelPercent: 51.6, fuelRangeKm: 432.4 }),
    )

    const rows = wrapper.findAll('.detail-list__item').map((r) => r.text())
    expect(rows).toEqual([`52%-${en.vehicle.fuel}`, `432 km-${en.vehicle.range}`])
  })

  it('shows where charging stands on a car that plugs in', async () => {
    const wrapper = await render(
      'battery',
      'phev',
      TWO_DAYS,
      status({
        evSocPercent: 64,
        isCharging: true,
        chargerConnected: true,
        remainingChargingTime: 45,
      }),
    )

    const text = wrapper.text()
    expect(text).toContain(en.vehicle.chargingYes)
    expect(text).toContain(`45 ${en.common.min}`)
  })

  it('leaves charging out on a hybrid that cannot plug in', async () => {
    const wrapper = await render(
      'battery',
      'hev',
      TWO_DAYS,
      status({ evSocPercent: 58, chargerConnected: false }),
    )

    expect(wrapper.findAll('.detail-list__item')).toHaveLength(1)
    expect(wrapper.text()).not.toContain(en.vehicle.hvBattery.charger)
  })
})

describe('LevelBadge', () => {
  it('opens the history, named for screen readers', async () => {
    const wrapper = mount(LevelBadge, {
      props: { icon: 'gas-pump', label: en.vehicle.fuel, percent: 51.6, variant: 'success' },
      global: { plugins: [i18n], stubs: { FontAwesomeIcon: true } },
    })

    const button = wrapper.get('button')
    expect(button.attributes('aria-label')).toBe('Fuel: 52%. Show history')
    await button.trigger('click')

    expect(wrapper.emitted('select')).toHaveLength(1)
  })
})
