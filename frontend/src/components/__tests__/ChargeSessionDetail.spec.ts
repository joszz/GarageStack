import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { enableAutoUnmount, mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { createPinia, setActivePinia } from 'pinia'
import en from '@/locales/en.json'
import ChargeSessionDetail from '../ChargeSessionDetail.vue'
import type { ChargeSession, ChargeSessionPowerPoint } from '@/services/vehicleApi'
import type { ChargeEnergyContext } from '@/utils/chargeSessions'

vi.mock('../ChargeSessionLocationMap.vue', () => ({
  default: { template: '<div class="map-stub" />', props: ['latitude', 'longitude', 'label'] },
}))

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

enableAutoUnmount(afterEach)

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

const POWER: ChargeSessionPowerPoint[] = [
  { recordedAt: '2026-10-20T08:00:00Z', powerKw: 74.5 },
  { recordedAt: '2026-10-20T09:00:00Z', powerKw: 30 },
]

const bev: ChargeEnergyContext = { vehicleType: 'bev', configuredCapacityKwh: null }
const phev: ChargeEnergyContext = { vehicleType: 'phev', configuredCapacityKwh: null }

function mountDetail(props: Partial<InstanceType<typeof ChargeSessionDetail>['$props']> = {}) {
  return mount(ChargeSessionDetail, {
    props: {
      session: current,
      previous,
      energyContext: bev,
      powerPoints: POWER,
      powerLoading: false,
      powerError: null,
      ...props,
    },
    global: {
      plugins: [i18n],
      stubs: {
        FontAwesomeIcon: true,
        StatsChartCard: { template: '<div class="chart-stub">{{ title }}</div>', props: ['title'] },
      },
    },
  })
}

/** The figure tiles under `selector`, as label: value. */
function figures(wrapper: ReturnType<typeof mountDetail>, selector = '.charge-history-detail') {
  return Object.fromEntries(
    wrapper
      .find(selector)
      .find('.detail-figures')
      .findAll('.detail-figure')
      .map((figure) => {
        // The unit is set apart by a margin, not a space.
        const unit = figure.find('.detail-figure__unit')
        const value = figure.find('.detail-figure__value').text()
        return [
          figure.find('.detail-figure__label').text(),
          unit.exists() ? `${value.slice(0, -unit.text().length)} ${unit.text()}` : value,
        ]
      }),
  )
}

beforeEach(() => {
  setActivePinia(createPinia())
})

describe('ChargeSessionDetail', () => {
  it('heads the detail with when it charged and the kind of charge', () => {
    const wrapper = mountDetail()

    const title = wrapper.find('.list-detail-item__title').text()
    expect(title).toContain('October')
    expect(title).toContain('2026')
    expect(wrapper.find('.charge-history-detail__kind').text()).toBe('DC')
    expect(wrapper.find('.charge-history-detail__kind').classes()).toContain('badge-info')
  })

  it('shows what the charge did', () => {
    expect(figures(mountDetail())).toEqual({
      [en.chargeHistory.energyAdded]: '29.0 kWh',
      [en.chargeHistory.duration]: '1h 30m',
      [en.chargeHistory.stateOfCharge]: '40% → 80%',
      [en.chargeHistory.peakPower]: '74.5 kW',
      [en.chargeHistory.odometer]: '10,200 km',
    })
  })

  it('works out the driving since the charge before on a BEV', () => {
    expect(figures(mountDetail(), '.charge-history-detail__since')).toEqual({
      [en.chargeHistory.distance]: '200.0 km',
      [en.chargeHistory.energyUsed]: '32.6 kWh',
      [en.chargeHistory.consumption]: '16.3 kWh/100 km',
    })
  })

  // MG's nominal 72.5 kWh pack is three times a PHEV's (issue #571), and its kilometres
  // include petrol ones.
  it('shows no kWh on a plug-in hybrid without a configured capacity', () => {
    const wrapper = mountDetail({ energyContext: phev })

    expect(figures(wrapper)[en.chargeHistory.energyAdded]).toBe('+40%')
    expect(figures(wrapper, '.charge-history-detail__since')).toEqual({
      [en.chargeHistory.distance]: '200.0 km',
      [en.chargeHistory.energyUsed]: '-',
    })
  })

  it('leaves out the driving since for the oldest session listed', () => {
    expect(mountDetail({ previous: null }).find('.charge-history-detail__since').exists()).toBe(
      false,
    )
  })

  it('leaves out what the car did not report, and says when it has no location', () => {
    const wrapper = mountDetail({
      session: {
        ...current,
        odometerKm: null,
        maxPowerKw: null,
        latitude: null,
        chargingType: null,
      },
      previous: null,
    })

    expect(Object.keys(figures(wrapper))).toEqual([
      en.chargeHistory.energyAdded,
      en.chargeHistory.duration,
      en.chargeHistory.stateOfCharge,
    ])
    expect(wrapper.find('.charge-history-detail__kind').exists()).toBe(false)
    expect(wrapper.find('.map-stub').exists()).toBe(false)
    expect(wrapper.text()).toContain(en.chargeHistory.locationUnavailable)
  })

  it('marks a session that is still going', () => {
    const wrapper = mountDetail({ session: { ...current, endedAt: null } })

    expect(wrapper.find('.list-detail-item__status .badge-success').text()).toBe(
      en.chargeHistory.ongoing,
    )
    expect(figures(wrapper)[en.chargeHistory.duration]).toBe(en.chargeHistory.ongoing)
  })

  it('shows the location and the power curve', () => {
    const wrapper = mountDetail()

    expect(wrapper.find('.map-stub').exists()).toBe(true)
    expect(wrapper.find('.chart-stub').text()).toBe(en.chargeHistory.power)
  })

  it('says so while the curve loads, and when the car reported no power', () => {
    expect(mountDetail({ powerPoints: null, powerLoading: true }).text()).toContain(
      en.common.loading,
    )
    expect(mountDetail({ powerPoints: [] }).text()).toContain(en.chargeHistory.noPowerData)
  })

  it('offers a retry when the curve failed to load', async () => {
    const wrapper = mountDetail({ powerPoints: null, powerError: 'offline' })

    expect(wrapper.find('[role="alert"]').text()).toContain('offline')
    await wrapper.find('[role="alert"] button').trigger('click')
    expect(wrapper.emitted('retry')).toHaveLength(1)
  })
})
