import { describe, it, expect, vi } from 'vitest'
import { ref } from 'vue'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import HvBatteryCard from '../HvBatteryCard.vue'

const send = vi.fn<(...args: unknown[]) => Promise<boolean>>().mockResolvedValue(true)

vi.mock('@/composables/useVehicleCommand', () => ({
  useVehicleCommand: () => ({
    sending: ref(null),
    lastResult: ref(null),
    isPending: () => false,
    send,
  }),
}))

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

function mountCard(props: Record<string, unknown> = {}) {
  return mount(HvBatteryCard, {
    props: {
      vin: 'FAKEVN00000000001',
      socPercent: 64,
      storedKwh: null,
      capacityKwh: null,
      hvVoltage: null,
      hvCurrent: null,
      hvPower: null,
      hvBatteryActive: null,
      chargerConnected: true,
      powerUsageSinceLastCharge: null,
      socTargetPercent: null,
      chargeCurrentLimit: null,
      canSetChargeLimit: true,
      ...props,
    },
    global: {
      plugins: [i18n],
      stubs: { DetailModal: { template: '<div><slot /></div>' }, FontAwesomeIcon: true },
    },
  })
}

function limitButtons(wrapper: ReturnType<typeof mountCard>) {
  return wrapper.findAll('.modal-btn-group button')
}

describe('HvBatteryCard', () => {
  it('shows the charge level the car stops at', () => {
    expect(mountCard({ socTargetPercent: 80 }).text()).toContain('Charge target')
  })

  // The gateway reports "Max" where the command takes "MAX".
  it('marks the limit the car charges at now', () => {
    const buttons = limitButtons(mountCard({ chargeCurrentLimit: 'Max' }))

    expect(buttons.map((b) => b.attributes('aria-pressed'))).toEqual([
      'false',
      'false',
      'false',
      'true',
    ])
    expect(buttons[3]!.classes()).toContain('btn-success')
    expect(buttons[0]!.classes()).toContain('btn-outline-secondary')
  })

  it('marks no limit while the car has not reported one', () => {
    const buttons = limitButtons(mountCard())

    expect(buttons.every((b) => b.attributes('aria-pressed') === 'false')).toBe(true)
  })

  it('sends the limit a button stands for', async () => {
    await limitButtons(mountCard())[1]!.trigger('click')

    expect(send).toHaveBeenCalledWith('FAKEVN00000000001', 'charge-limit', '8A')
  })
})
