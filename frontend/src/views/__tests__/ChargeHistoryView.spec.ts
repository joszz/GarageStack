import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { enableAutoUnmount, flushPromises, mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { createPinia, setActivePinia } from 'pinia'
import en from '@/locales/en.json'
import ChargeHistoryView from '../ChargeHistoryView.vue'
import {
  vehicleApi,
  type ChargeSession,
  type Vehicle,
  type VehicleType,
} from '@/services/vehicleApi'

const replace = vi.fn<(to: unknown) => Promise<void>>()

vi.mock('vue-router', () => ({ useRouter: () => ({ replace }) }))

vi.mock('@/services/vehicleApi', () => ({
  vehicleApi: {
    list: vi.fn<() => Promise<Vehicle[]>>(),
    chargeSessions: vi.fn<() => Promise<ChargeSession[]>>(),
  },
}))

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

enableAutoUnmount(afterEach)

function vehicle(vehicleType: VehicleType): Vehicle {
  return {
    id: 1,
    vin: 'FAKEVN00000000001',
    model: 'ZS EV',
    series: null,
    createdAt: '2026-01-01T00:00:00Z',
    vehicleType,
    hvBatteryCapacityKwh: null,
    supportsTargetSoc: true,
    hasPowerTailgate: false,
  }
}

function session(startedAt: string, endedAt: string | null): ChargeSession {
  return {
    startedAt,
    endedAt,
    startSocPercent: 40,
    endSocPercent: 80,
    chargingType: 'AC',
    odometerKm: 10000,
    latitude: 52.37,
    longitude: 4.9,
    startBatteryKwh: 28,
    endBatteryKwh: 56,
    batteryEnergyGainedKwh: 28,
    maxPowerKw: 7.2,
  }
}

const ItemStub = {
  name: 'ChargeSessionItem',
  props: ['vin', 'session', 'previous', 'energyContext'],
  template: '<li class="item-stub">{{ session.startedAt }}|{{ previous?.startedAt ?? "-" }}</li>',
}

function mountView() {
  return mount(ChargeHistoryView, {
    global: {
      plugins: [i18n],
      stubs: {
        FontAwesomeIcon: true,
        ViewHeader: { template: '<header><h1>{{ title }}</h1></header>', props: ['title'] },
        StatusCard: {
          template: '<div class="summary">{{ label }} {{ value }}</div>',
          props: ['label', 'value'],
        },
        ChargeSessionItem: ItemStub,
      },
    },
  })
}

beforeEach(() => {
  localStorage.clear()
  setActivePinia(createPinia())
  vi.clearAllMocks()
  vi.mocked(vehicleApi.list).mockResolvedValue([vehicle('bev')])
  vi.mocked(vehicleApi.chargeSessions).mockResolvedValue([
    session('2026-10-20T08:00:00Z', null),
    session('2026-10-19T10:00:00Z', '2026-10-19T11:00:00Z'),
  ])
})

describe('ChargeHistoryView', () => {
  it('asks for the last 90 days and pairs each session with the one before it', async () => {
    const wrapper = mountView()
    await flushPromises()

    expect(vehicleApi.chargeSessions).toHaveBeenCalledTimes(1)
    const [vin, from, to] = vi.mocked(vehicleApi.chargeSessions).mock.calls[0]!
    expect(vin).toBe('FAKEVN00000000001')
    expect(Date.parse(to!) - Date.parse(from!)).toBe(90 * 24 * 60 * 60 * 1000)
    expect(wrapper.findAll('.item-stub').map((item) => item.text())).toEqual([
      '2026-10-20T08:00:00Z|2026-10-19T10:00:00Z',
      '2026-10-19T10:00:00Z|-',
    ])
    expect(wrapper.find('.summary').text()).toBe('Sessions 2')
  })

  it('asks again for another period', async () => {
    const wrapper = mountView()
    await flushPromises()

    await wrapper.find('select').setValue('365')
    await flushPromises()

    const [, from, to] = vi.mocked(vehicleApi.chargeSessions).mock.calls[1]!
    expect(Date.parse(to!) - Date.parse(from!)).toBe(365 * 24 * 60 * 60 * 1000)
  })

  it('sends a plain hybrid to the dashboard without asking for sessions', async () => {
    vi.mocked(vehicleApi.list).mockResolvedValue([vehicle('hev')])
    mountView()
    await flushPromises()

    expect(replace).toHaveBeenCalledWith({ name: 'dashboard' })
    expect(vehicleApi.chargeSessions).not.toHaveBeenCalled()
  })

  it('shows the empty state when no sessions have been recorded', async () => {
    vi.mocked(vehicleApi.chargeSessions).mockResolvedValue([])
    const wrapper = mountView()
    await flushPromises()

    expect(wrapper.text()).toContain(en.chargeHistory.empty)
    expect(wrapper.find('.charge-history-list').exists()).toBe(false)
  })

  it('offers a retry when loading fails', async () => {
    vi.mocked(vehicleApi.chargeSessions).mockRejectedValueOnce(new Error('offline'))
    const wrapper = mountView()
    await flushPromises()

    expect(wrapper.find('[role="alert"]').exists()).toBe(true)
    await wrapper.find('[role="alert"] button').trigger('click')
    await flushPromises()
    expect(wrapper.findAll('.item-stub')).toHaveLength(2)
  })
})
