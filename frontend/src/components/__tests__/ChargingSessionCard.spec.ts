import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import ChargingSessionCard from '../ChargingSessionCard.vue'
import BatteryHeatingCard from '../BatteryHeatingCard.vue'

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })
const global = {
  plugins: [i18n],
  stubs: { DetailModal: { template: '<div><slot /></div>' }, FontAwesomeIcon: true },
}

function mountCard(props: Record<string, unknown> = {}) {
  return mount(ChargingSessionCard, {
    props: {
      chargingType: null,
      chargingCableLock: null,
      obcPowerSinglePhase: null,
      obcPowerThreePhase: null,
      remainingChargingTime: null,
      bmsChargeStatus: null,
      lastChargeEndingPower: null,
      chargingLastStartAt: null,
      chargingLastEndAt: null,
      chargingScheduleMode: null,
      chargingScheduleStartTime: null,
      chargingScheduleEndTime: null,
      ...props,
    },
    global,
  })
}

describe('ChargingSessionCard', () => {
  it("words the car's charging state instead of the gateway's code", () => {
    const text = mountCard({ bmsChargeStatus: 'CONNECTED_NOT_CHARGING' }).text()

    expect(text).toContain('Connected, not charging')
    expect(text).not.toContain('CONNECTED_NOT_CHARGING')
  })

  it("words the charging schedule's mode", () => {
    const text = mountCard({
      bmsChargeStatus: 'UNPLUGGED',
      chargingScheduleMode: 'UNTIL_CONFIGURED_TIME',
      chargingScheduleStartTime: '23:00',
    }).text()

    expect(text).toContain('Until the set time')
  })

  it('shows when the last charge started next to when it ended', () => {
    const text = mountCard({
      bmsChargeStatus: 'CHARGE_DONE',
      chargingLastStartAt: '2026-10-10T22:00:00Z',
      chargingLastEndAt: '2026-10-11T04:30:00Z',
    }).text()

    expect(text).toContain('Last charge started')
    expect(text).toContain('Last charge ended')
  })
})

describe('BatteryHeatingCard', () => {
  it("words the schedule's on and off", () => {
    const text = mount(BatteryHeatingCard, {
      props: { batteryHeating: false, scheduleMode: 'off', scheduleStartTime: '06:00' },
      global,
    }).text()

    expect(text).toContain('Off')
    expect(text).not.toContain('off')
  })
})
