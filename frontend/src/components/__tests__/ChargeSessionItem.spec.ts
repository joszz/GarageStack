import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { enableAutoUnmount, flushPromises, mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { createPinia, setActivePinia } from 'pinia'
import en from '@/locales/en.json'
import ChargeSessionItem from '../ChargeSessionItem.vue'
import { vehicleApi, type ChargeSession, type ChargeSessionPowerPoint } from '@/services/vehicleApi'
import type { ChargeEnergyContext } from '@/utils/chargeSessions'

vi.mock('@/services/vehicleApi', () => ({
  vehicleApi: { chargePower: vi.fn<() => Promise<ChargeSessionPowerPoint[]>>() },
}))

vi.mock('../ChargeSessionLocationMap.vue', () => ({
  default: { template: '<div class="map-stub" />', props: ['latitude', 'longitude', 'label'] },
}))

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

enableAutoUnmount(afterEach)

const VIN = 'FAKEVN00000000001'

const current: ChargeSession = {
  startedAt: '2026-10-20T08:00:00Z',
  endedAt: '2026-10-20T09:30:00Z',
  startSocPercent: 40,
  endSocPercent: 80,
  chargingType: 'DC',
  odometerKm: 10200,
  latitude: 52.4,
  longitude: 4.95,
  startBatteryKwh: 29,
  endBatteryKwh: 58,
  batteryEnergyGainedKwh: 29,
  maxPowerKw: 74.5,
}

const previous: ChargeSession = {
  ...current,
  startedAt: '2026-10-18T19:00:00Z',
  endedAt: '2026-10-18T23:00:00Z',
  chargingType: 'AC',
  odometerKm: 10000,
  endSocPercent: 85,
  endBatteryKwh: 61.6,
}

function mountItem(
  energyContext: ChargeEnergyContext,
  session: ChargeSession = current,
  before: ChargeSession | null = previous,
) {
  return mount(ChargeSessionItem, {
    props: { vin: VIN, session, previous: before, energyContext },
    global: {
      plugins: [i18n],
      stubs: {
        FontAwesomeIcon: true,
        StatsChartCard: { template: '<div class="chart-stub">{{ title }}</div>', props: ['title'] },
      },
    },
  })
}

function detail(wrapper: ReturnType<typeof mountItem>, label: string): string | undefined {
  return wrapper
    .findAll('dl > div')
    .find((row) => row.find('dt').text() === label)
    ?.find('dd')
    .text()
}

const bev: ChargeEnergyContext = { vehicleType: 'bev', configuredCapacityKwh: null }
const phev: ChargeEnergyContext = { vehicleType: 'phev', configuredCapacityKwh: null }

beforeEach(() => {
  setActivePinia(createPinia())
  vi.clearAllMocks()
  vi.mocked(vehicleApi.chargePower).mockResolvedValue([
    { recordedAt: '2026-10-20T08:00:00Z', powerKw: 74.5 },
    { recordedAt: '2026-10-20T09:00:00Z', powerKw: 30 },
  ])
})

describe('ChargeSessionItem', () => {
  it('shows the session at a glance', () => {
    const wrapper = mountItem(bev)

    expect(detail(wrapper, en.chargeHistory.duration)).toBe('1h 30m')
    expect(detail(wrapper, en.chargeHistory.stateOfCharge)).toBe('40% → 80%')
    expect(detail(wrapper, en.chargeHistory.odometer)).toBe('10,200 km')
    expect(detail(wrapper, en.chargeHistory.batteryEnergyGained)).toBe('29.0 kWh')
    expect(detail(wrapper, en.chargeHistory.maxPower)).toBe('74.5 kW')
    expect(wrapper.find('.charge-history-item__type--dc').text()).toBe('DC')
    expect(wrapper.find('.charge-history-detail').exists()).toBe(false)
  })

  it('loads the power curve once, when first opened', async () => {
    const wrapper = mountItem(bev)
    const toggle = wrapper.find('.charge-history-item__toggle')

    await toggle.trigger('click')
    await flushPromises()
    expect(vehicleApi.chargePower).toHaveBeenCalledWith(VIN, current.startedAt, current.endedAt)
    expect(wrapper.find('.chart-stub').text()).toBe(en.chargeHistory.power)
    expect(toggle.attributes('aria-expanded')).toBe('true')
    expect(wrapper.find('.map-stub').exists()).toBe(true)

    await toggle.trigger('click')
    await toggle.trigger('click')
    expect(vehicleApi.chargePower).toHaveBeenCalledTimes(1)
  })

  it('asks for an ongoing session up to now', async () => {
    const wrapper = mountItem(bev, { ...current, endedAt: null })
    await wrapper.find('.charge-history-item__toggle').trigger('click')

    expect(vehicleApi.chargePower).toHaveBeenCalledWith(VIN, current.startedAt, undefined)
  })

  it('says so when the car reported no power', async () => {
    vi.mocked(vehicleApi.chargePower).mockResolvedValue([])
    const wrapper = mountItem(bev)
    await wrapper.find('.charge-history-item__toggle').trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain(en.chargeHistory.noPowerData)
  })

  it('offers a retry when the curve fails to load', async () => {
    vi.mocked(vehicleApi.chargePower).mockRejectedValueOnce(new Error('offline'))
    const wrapper = mountItem(bev)
    await wrapper.find('.charge-history-item__toggle').trigger('click')
    await flushPromises()

    await wrapper.find('[role="alert"] button').trigger('click')
    await flushPromises()
    expect(wrapper.find('.chart-stub').exists()).toBe(true)
  })

  it('works out the driving since the charge before on a BEV', async () => {
    const wrapper = mountItem(bev)
    await wrapper.find('.charge-history-item__toggle').trigger('click')
    await flushPromises()

    expect(detail(wrapper, en.chargeHistory.distanceSincePreviousCharge)).toBe('200.0 km')
    expect(detail(wrapper, en.chargeHistory.energyUsedSincePreviousCharge)).toBe('32.6 kWh')
    expect(detail(wrapper, en.chargeHistory.consumptionSincePreviousCharge)).toBe('16.3 kWh/100 km')
  })

  // MG's nominal 72.5 kWh pack is three times a PHEV's (issue #571), and its kilometres
  // include petrol ones.
  it('shows no kWh on a plug-in hybrid without a configured capacity', async () => {
    const wrapper = mountItem(phev)
    await wrapper.find('.charge-history-item__toggle').trigger('click')
    await flushPromises()

    expect(detail(wrapper, en.chargeHistory.batteryEnergyGained)).toBe(en.chargeHistory.notReported)
    expect(detail(wrapper, en.chargeHistory.energyUsedSincePreviousCharge)).toBe(
      en.chargeHistory.notReported,
    )
    expect(detail(wrapper, en.chargeHistory.consumptionSincePreviousCharge)).toBeUndefined()
  })

  it('takes a configured capacity on a plug-in hybrid', () => {
    const wrapper = mountItem({ vehicleType: 'phev', configuredCapacityKwh: 20 })

    expect(detail(wrapper, en.chargeHistory.batteryEnergyGained)).toBe('8.0 kWh')
  })

  it('shows a placeholder where the car reported nothing', () => {
    const wrapper = mountItem(
      bev,
      {
        ...current,
        startSocPercent: null,
        odometerKm: null,
        maxPowerKw: null,
        latitude: null,
        chargingType: null,
      },
      null,
    )

    expect(detail(wrapper, en.chargeHistory.stateOfCharge)).toBe(
      `${en.chargeHistory.notReported} → 80%`,
    )
    expect(detail(wrapper, en.chargeHistory.odometer)).toBe(en.chargeHistory.notReported)
    expect(detail(wrapper, en.chargeHistory.maxPower)).toBe(en.chargeHistory.notReported)
    expect(wrapper.find('.charge-history-item__type').exists()).toBe(false)
  })
})
