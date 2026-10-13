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
      canSetChargeTarget: false,
      ...props,
    },
    global: {
      plugins: [i18n],
      stubs: { DetailModal: { template: '<div><slot /></div>' }, FontAwesomeIcon: true },
    },
  })
}

function limitButtons(wrapper: ReturnType<typeof mountCard>) {
  return wrapper.findAll('.modal-btn-group button[aria-pressed]')
}

function targetButton(wrapper: ReturnType<typeof mountCard>) {
  return wrapper.find('.modal-btn-group button:not([aria-pressed])')
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

  it('offers a charge target only on a car that takes one', () => {
    expect(mountCard().find('input[type="range"]').exists()).toBe(false)
    expect(mountCard({ canSetChargeTarget: true }).find('input[type="range"]').exists()).toBe(true)
  })

  it("starts the target slider at the car's target, and sends a new one", async () => {
    const wrapper = mountCard({ canSetChargeTarget: true, socTargetPercent: 80 })
    const slider = wrapper.find<HTMLInputElement>('input[type="range"]')
    expect(slider.element.value).toBe('80')
    expect(targetButton(wrapper).attributes('disabled')).toBeDefined()

    await slider.setValue('90')
    await targetButton(wrapper).trigger('click')

    const [vin, command, value, options] = send.mock.lastCall! as [
      string,
      string,
      string,
      { isConfirmed: (s: { socTargetPercent: number | null }) => boolean },
    ]
    expect([vin, command, value]).toEqual(['FAKEVN00000000001', 'charge-target', '90'])
    // Taken as done once the car reports the new target, not on the gateway's word alone.
    expect(options.isConfirmed({ socTargetPercent: 80 })).toBe(false)
    expect(options.isConfirmed({ socTargetPercent: 90 })).toBe(true)
  })

  it('starts the slider at 80% while the car has not reported a target', () => {
    const wrapper = mountCard({ canSetChargeTarget: true })

    expect(wrapper.find<HTMLInputElement>('input[type="range"]').element.value).toBe('80')
  })
})
