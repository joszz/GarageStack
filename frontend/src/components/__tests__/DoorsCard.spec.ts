import { describe, it, expect, vi, beforeEach } from 'vitest'
import { ref } from 'vue'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import type { SendOptions } from '@/composables/useVehicleCommand'
import type { TelemetrySnapshot } from '@/services/vehicleApi'
import DoorsCard from '../DoorsCard.vue'

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
const VIN = 'FAKEVN00000000001'

function mountCard(props: Record<string, unknown> = {}) {
  return mount(DoorsCard, {
    props: {
      vin: VIN,
      isLocked: true,
      driverDoorOpen: false,
      passengerDoorOpen: false,
      rearLeftDoorOpen: false,
      rearRightDoorOpen: false,
      bonnetOpen: false,
      trunkOpen: false,
      ...props,
    },
    global: {
      plugins: [i18n],
      stubs: { DetailModal: { template: '<div><slot /></div>' }, FontAwesomeIcon: true },
    },
  })
}

function buttonLabels(wrapper: ReturnType<typeof mountCard>) {
  return wrapper.findAll('button').map((b) => b.text())
}

function bootButton(wrapper: ReturnType<typeof mountCard>) {
  return wrapper
    .findAll('button')
    .find((b) => b.text() === 'Open' || b.text() === 'Tap again to open')!
}

beforeEach(() => {
  send.mockReset()
  send.mockResolvedValue(true)
})

describe('DoorsCard', () => {
  it('opens the boot only at a second tap, until the car reports it open', async () => {
    const wrapper = mountCard()

    await bootButton(wrapper).trigger('click')
    expect(send).not.toHaveBeenCalled()
    expect(bootButton(wrapper).text()).toBe('Tap again to open')

    await bootButton(wrapper).trigger('click')
    const [vin, command, value, options] = send.mock.lastCall!
    expect([vin, command, value]).toEqual([VIN, 'boot', 'false'])
    expect(options?.isConfirmed?.({ trunkOpen: true } as TelemetrySnapshot)).toBe(true)
  })

  // The gateway can open the boot but never close it.
  it('offers nothing for a boot that is open, or not reported', () => {
    expect(buttonLabels(mountCard({ trunkOpen: true }))).not.toContain('Open')
    expect(buttonLabels(mountCard({ trunkOpen: null }))).not.toContain('Open')
  })

  it('offers the boot on a car that has not reported its locks yet', () => {
    expect(buttonLabels(mountCard({ isLocked: null }))).toEqual(['Open'])
  })
})
