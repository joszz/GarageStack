import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { enableAutoUnmount, flushPromises, mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { createPinia, setActivePinia } from 'pinia'
import en from '@/locales/en.json'
import ChargeHistoryView from '../ChargeHistoryView.vue'
import { vehicleApi, type ChargeSession, type Vehicle } from '@/services/vehicleApi'

vi.mock('@/services/vehicleApi', () => ({
  vehicleApi: {
    list: vi.fn<() => Promise<Vehicle[]>>(),
    chargeSessions: vi.fn<() => Promise<ChargeSession[]>>(),
  },
}))

vi.mock('@/components/ChargeSessionLocationMap.vue', () => ({
  default: {
    template: '<div class="charge-history-location-map" />',
    props: ['latitude', 'longitude', 'label'],
  },
}))

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

enableAutoUnmount(afterEach)

const vehicle: Vehicle = {
  id: 1,
  vin: 'FAKEVN00000000001',
  model: 'ZS EV',
  series: null,
  createdAt: '2026-01-01T00:00:00Z',
  vehicleType: 'bev',
  hvBatteryCapacityKwh: null,
  supportsTargetSoc: true,
  hasPowerTailgate: false,
}

beforeEach(() => {
  localStorage.clear()
  setActivePinia(createPinia())
  vi.clearAllMocks()
  vi.mocked(vehicleApi.list).mockResolvedValue([vehicle])
  vi.mocked(vehicleApi.chargeSessions).mockResolvedValue([
    {
      startedAt: '2026-10-20T08:00:00Z',
      endedAt: null,
      startSocPercent: 45,
      endSocPercent: 52,
      chargingType: 'DC',
      odometerKm: 10022,
      latitude: 52.4,
      longitude: 4.95,
      startBatteryKwh: 20,
      endBatteryKwh: 23,
      batteryEnergyGainedKwh: 3,
      maxPowerKw: 7.2,
      powerPoints: [
        { recordedAt: '2026-10-20T08:00:00Z', powerKw: 7.2 },
        { recordedAt: '2026-10-20T08:30:00Z', powerKw: 6.4 },
      ],
    },
    {
      startedAt: '2026-10-19T10:00:00Z',
      endedAt: '2026-10-19T11:00:00Z',
      startSocPercent: 35,
      endSocPercent: 80,
      chargingType: 'AC',
      odometerKm: 10002,
      latitude: 52.3676,
      longitude: 4.9041,
      startBatteryKwh: 20,
      endBatteryKwh: 27,
      batteryEnergyGainedKwh: 7,
      maxPowerKw: 11,
      powerPoints: [{ recordedAt: '2026-10-19T10:00:00Z', powerKw: 7.2 }],
    },
  ])
})

describe('ChargeHistoryView', () => {
  it('loads and displays completed and ongoing sessions', async () => {
    const wrapper = mount(ChargeHistoryView, {
      global: {
        plugins: [i18n],
        stubs: {
          FontAwesomeIcon: true,
          ViewHeader: { template: '<header><h1>{{ title }}</h1></header>', props: ['title'] },
          StatusCard: {
            template: '<div class="summary">{{ label }} {{ value }}</div>',
            props: ['label', 'value'],
          },
        },
      },
    })
    await flushPromises()

    expect(vehicleApi.chargeSessions).toHaveBeenCalledWith(vehicle.vin)
    expect(wrapper.findAll('.charge-history-item')).toHaveLength(2)
    expect(wrapper.text()).toContain('Completed')
    expect(wrapper.text()).toContain('In progress')
    expect(wrapper.text()).toContain('35% → 80%')
    expect(wrapper.text()).toContain('45% → 52%')
    expect(wrapper.text()).toContain('7.2 kW')
    expect(wrapper.text()).toContain('3.0 kWh')
    expect(wrapper.text()).toContain('10,022 km')
    expect(wrapper.findAll('.charge-history-item__type')[1]?.text()).toBe('AC')
    expect(wrapper.find('.charge-history-item__type--dc').text()).toContain('DC')
    expect(wrapper.find('.charge-history-graph').exists()).toBe(false)

    await wrapper.find('.charge-history-item__graph-toggle').trigger('click')
    expect(wrapper.find('.charge-history-graph__chart').exists()).toBe(true)
    const axisTitles = wrapper
      .findAll('.charge-history-graph__axis-title')
      .map((element) => element.text())
      .join(' ')
    expect(axisTitles).toContain('Power (kW)')
    expect(axisTitles).toContain('Time')
    expect(wrapper.find('.charge-history-graph__metrics').text()).toContain('20 km')
    expect(wrapper.find('.charge-history-graph__metrics').text()).toContain('7.0 kWh')
    expect(wrapper.find('.charge-history-graph__metrics').text()).toContain('35.0 kWh/100 km')
    const graphPoints = wrapper.findAll('.charge-history-graph__point')
    expect(graphPoints[graphPoints.length - 1]?.attributes('cx')).toBe('372')
    expect(wrapper.find('.charge-history-location-map').exists()).toBe(true)
    expect(wrapper.find('.charge-history-item__graph-toggle').attributes('aria-expanded')).toBe(
      'true',
    )
    await wrapper.find('.charge-history-item__graph-toggle').trigger('click')
    expect(wrapper.find('.charge-history-graph').exists()).toBe(false)
  })

  it('shows the empty state when no sessions have been recorded', async () => {
    vi.mocked(vehicleApi.chargeSessions).mockResolvedValue([])
    const wrapper = mount(ChargeHistoryView, {
      global: {
        plugins: [i18n],
        stubs: { FontAwesomeIcon: true, ViewHeader: true },
      },
    })
    await flushPromises()

    expect(wrapper.text()).toContain('No charge sessions are available yet.')
    expect(wrapper.find('.charge-history-list').exists()).toBe(false)
  })
})
