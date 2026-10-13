import { describe, it, expect, vi, beforeEach } from 'vitest'
import { ref } from 'vue'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import type { SendOptions } from '@/composables/useVehicleCommand'
import type { TelemetrySnapshot } from '@/services/vehicleApi'
import ChargingSessionCard from '../ChargingSessionCard.vue'
import BatteryHeatingCard from '../BatteryHeatingCard.vue'

const send =
  vi.fn<
    (vin: string | null, command: string, value: string, options?: SendOptions) => Promise<boolean>
  >()

vi.mock('@/composables/useVehicleCommand', () => ({
  useVehicleCommand: () => ({
    sending: ref(null),
    lastResult: ref(null),
    isPending: () => false,
    send,
  }),
}))

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })
const global = {
  plugins: [i18n],
  stubs: { DetailModal: { template: '<div><slot /></div>' }, FontAwesomeIcon: true },
}
const VIN = 'FAKEVN00000000001'

function mountCard(props: Record<string, unknown> = {}) {
  return mount(ChargingSessionCard, {
    props: {
      vin: VIN,
      isCharging: null,
      chargerConnected: null,
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
      canChargeUntilTarget: false,
      ...props,
    },
    global,
  })
}

function button(wrapper: ReturnType<typeof mount>, label: string) {
  const found = wrapper.findAll('button').find((b) => b.text() === label)
  if (!found) throw new Error(`No button "${label}" in: ${wrapper.text()}`)
  return found
}

// What the last send would take as done, asked of a snapshot holding only `fields`.
function confirmedBy(fields: Partial<TelemetrySnapshot>): boolean | undefined {
  return send.mock.lastCall?.[3]?.isConfirmed?.(fields as TelemetrySnapshot)
}

beforeEach(() => {
  send.mockReset()
  send.mockResolvedValue(true)
})

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

  it('starts charging a car that is plugged in, until it reports charging', async () => {
    const wrapper = mountCard({ isCharging: false, chargerConnected: true })

    await button(wrapper, 'Start').trigger('click')

    expect(send.mock.lastCall?.slice(0, 3)).toEqual([VIN, 'charging', 'true'])
    expect(confirmedBy({ isCharging: false })).toBe(false)
    expect(confirmedBy({ isCharging: true })).toBe(true)
  })

  it('stops a charge that is running', async () => {
    const wrapper = mountCard({ isCharging: true, chargerConnected: true })

    await button(wrapper, 'Stop').trigger('click')

    expect(send.mock.lastCall?.slice(0, 3)).toEqual([VIN, 'charging', 'false'])
  })

  it('asks for the cable before a charge can start', () => {
    const wrapper = mountCard({ isCharging: false, chargerConnected: false })

    expect(button(wrapper, 'Start').attributes('disabled')).toBeDefined()
    expect(wrapper.text()).toContain('Plug the car in to start charging.')
  })

  it('locks the cable at one tap', async () => {
    const wrapper = mountCard({ chargingCableLock: false })

    await button(wrapper, 'Lock').trigger('click')

    expect(send.mock.lastCall?.slice(0, 3)).toEqual([VIN, 'charging-cable-lock', 'true'])
    expect(confirmedBy({ chargingCableLock: true })).toBe(true)
  })

  // Anyone holding an unlocked phone could free the cable to take it.
  it('unlocks the cable only at a second tap', async () => {
    const wrapper = mountCard({ chargingCableLock: true })

    await button(wrapper, 'Unlock').trigger('click')
    expect(send).not.toHaveBeenCalled()

    await button(wrapper, 'Tap again to unlock').trigger('click')
    expect(send.mock.lastCall?.slice(0, 3)).toEqual([VIN, 'charging-cable-lock', 'false'])
  })

  it('offers no charging button before the car reports whether it charges', () => {
    const wrapper = mountCard({ bmsChargeStatus: 'UNPLUGGED' })

    expect(wrapper.findAll('button').map((b) => b.text())).not.toContain('Start')
  })
})

describe('BatteryHeatingCard', () => {
  function mountHeating(props: Record<string, unknown> = {}) {
    return mount(BatteryHeatingCard, {
      props: {
        vin: VIN,
        batteryHeating: false,
        scheduleMode: null,
        scheduleStartTime: null,
        ...props,
      },
      global,
    })
  }

  it("words the schedule's on and off", () => {
    const text = mountHeating({ scheduleMode: 'off', scheduleStartTime: '06:00' }).text()

    expect(text).toContain('Off')
    expect(text).not.toContain('off')
  })

  it('switches the heating on, until the car reports it on', async () => {
    await button(mountHeating(), 'Heat').trigger('click')

    expect(send.mock.lastCall?.slice(0, 3)).toEqual([VIN, 'battery-heating', 'true'])
    expect(confirmedBy({ batteryHeating: true })).toBe(true)
  })

  it('switches running heating off', async () => {
    await button(mountHeating({ batteryHeating: true }), 'Stop').trigger('click')

    expect(send.mock.lastCall?.slice(0, 3)).toEqual([VIN, 'battery-heating', 'false'])
  })
})
