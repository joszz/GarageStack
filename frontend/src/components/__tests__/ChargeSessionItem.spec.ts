import { afterEach, describe, expect, it } from 'vitest'
import { enableAutoUnmount, mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { createPinia } from 'pinia'
import en from '@/locales/en.json'
import ChargeSessionItem from '../ChargeSessionItem.vue'
import type { ChargeSession } from '@/services/vehicleApi'
import type { ChargeEnergyContext } from '@/utils/chargeSessions'

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

const bev: ChargeEnergyContext = { vehicleType: 'bev', configuredCapacityKwh: null }
const phev: ChargeEnergyContext = { vehicleType: 'phev', configuredCapacityKwh: null }

function mountItem(
  energyContext: ChargeEnergyContext,
  session: ChargeSession = current,
  selected = false,
) {
  return mount(ChargeSessionItem, {
    props: { session, energyContext, selected },
    global: { plugins: [i18n, createPinia()], stubs: { FontAwesomeIcon: true } },
  })
}

describe('ChargeSessionItem', () => {
  it('shows the session at a glance, in two lines', () => {
    const wrapper = mountItem(bev)

    expect(wrapper.find('.list-row__pill').text()).toBe('1h 30m')
    expect(wrapper.find('.charge-history-item__energy').text()).toBe('29.0 kWh')
    expect(wrapper.find('.charge-history-item__soc').text()).toBe('40% → 80%')
    expect(wrapper.find('.charge-history-item__power').text()).toBe('74.5 kW')
    expect(wrapper.find('.list-row__icon').classes()).toContain('badge-info')
    expect(wrapper.find('.list-row__icon').attributes('aria-label')).toBe('DC charge')
    expect(wrapper.find('.list-row__chevron').attributes('icon')).toBe('chevron-right')
  })

  it('leaves the odometer and the kind of charge to the detail', () => {
    const wrapper = mountItem(bev)

    expect(wrapper.text()).not.toContain('10,200')
    expect(wrapper.text()).not.toMatch(/\bDC\b/)
  })

  it('marks a session that is still going', () => {
    const wrapper = mountItem(bev, { ...current, endedAt: null })

    expect(wrapper.find('.list-row__pill--active').text()).toBe(en.chargeHistory.ongoing)
    expect(wrapper.find('.list-row__end-time').exists()).toBe(false)
    expect(wrapper.find('.list-row__icon').classes()).toContain('badge-success')
  })

  it('asks for its detail when picked, and shows when it is the one shown', async () => {
    const wrapper = mountItem(bev)
    await wrapper.find('.list-row__select').trigger('click')
    expect(wrapper.emitted('select')).toHaveLength(1)
    expect(wrapper.classes()).not.toContain('list-row--selected')

    const selected = mountItem(bev, current, true)
    expect(selected.classes()).toContain('list-row--selected')
    expect(selected.find('.list-row__select').attributes('aria-pressed')).toBe('true')
  })

  // MG's nominal 72.5 kWh pack is three times a PHEV's (issue #571).
  it('shows the percentage points gained on a plug-in hybrid without a configured capacity', () => {
    expect(mountItem(phev).find('.charge-history-item__energy').text()).toBe('+40%')
  })

  it('takes a configured capacity on a plug-in hybrid', () => {
    const wrapper = mountItem({ vehicleType: 'phev', configuredCapacityKwh: 20 })

    expect(wrapper.find('.charge-history-item__energy').text()).toBe('8.0 kWh')
  })

  it('reads the raw charging type the gateway passes on', () => {
    const wrapper = mountItem(bev, { ...current, chargingType: '6' })

    expect(wrapper.find('.list-row__icon').classes()).toContain('badge-secondary')
    expect(wrapper.find('.list-row__icon').attributes('aria-label')).toBe('AC charge')
  })

  it('leaves out what the car did not report', () => {
    const wrapper = mountItem(bev, {
      ...current,
      startSocPercent: null,
      maxPowerKw: null,
      chargingType: null,
    })

    // Only the end of the charge is known, so that is all the row says about it.
    expect(wrapper.find('.charge-history-item__soc').text()).toBe('80%')
    expect(wrapper.find('.charge-history-item__power').exists()).toBe(false)
    expect(wrapper.find('.list-row__icon').classes()).toContain('badge-secondary')
    expect(wrapper.find('.list-row__icon').attributes('aria-label')).toBe(en.chargeHistory.session)
  })
})
