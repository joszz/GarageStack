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
}

beforeEach(() => {
  localStorage.clear()
  setActivePinia(createPinia())
  vi.clearAllMocks()
  vi.mocked(vehicleApi.list).mockResolvedValue([vehicle])
  vi.mocked(vehicleApi.chargeSessions).mockResolvedValue([
    {
      startedAt: '2026-10-19T10:00:00Z',
      endedAt: '2026-10-19T11:00:00Z',
      startSocPercent: 35,
      endSocPercent: 80,
      chargingType: 'AC',
    },
    {
      startedAt: '2026-10-20T08:00:00Z',
      endedAt: null,
      startSocPercent: 45,
      endSocPercent: 52,
      chargingType: 'AC',
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
