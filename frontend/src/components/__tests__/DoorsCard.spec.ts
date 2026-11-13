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

// Read on each render, so a test can hold a command pending and then change the props.
const pending = new Set<string>()

vi.mock('@/composables/useVehicleCommand', () => ({
  useVehicleCommand: () => ({
    sending: ref(null),
    lastResult: ref(null),
    isPending: (key: string) => pending.has(key),
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

function buttonLabelled(wrapper: ReturnType<typeof mountCard>, ...labels: string[]) {
  return wrapper.findAll('button').find((b) => labels.includes(b.text()))!
}

beforeEach(() => {
  send.mockReset()
  send.mockResolvedValue(true)
  pending.clear()
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

  describe('with a power tailgate', () => {
    it('opens it at a second tap, instead of unlatching it', async () => {
      const wrapper = mountCard({ hasPowerTailgate: true, isLocked: null })
      expect(buttonLabels(wrapper)).toEqual(['Open'])

      await buttonLabelled(wrapper, 'Open').trigger('click')
      expect(send).not.toHaveBeenCalled()

      await buttonLabelled(wrapper, 'Tap again to open').trigger('click')
      const [vin, command, value, options] = send.mock.lastCall!
      expect([vin, command, value]).toEqual([VIN, 'power-tailgate', 'open'])
      expect(options?.isConfirmed?.({ trunkOpen: true } as TelemetrySnapshot)).toBe(true)
      expect(options?.isConfirmed?.({ trunkOpen: false } as TelemetrySnapshot)).toBe(false)
    })

    it('closes it while the car reports it open', async () => {
      const wrapper = mountCard({ hasPowerTailgate: true, isLocked: null, trunkOpen: true })
      expect(buttonLabels(wrapper)).toEqual(['Close'])

      await buttonLabelled(wrapper, 'Close').trigger('click')
      await buttonLabelled(wrapper, 'Tap again to close').trigger('click')
      const [, command, value, options] = send.mock.lastCall!
      expect([command, value]).toEqual(['power-tailgate', 'close'])
      expect(options?.isConfirmed?.({ trunkOpen: false } as TelemetrySnapshot)).toBe(true)
    })

    // A reading without the boot's state arrives while the move is pending: the row stays, so the
    // waiting line and any refusal still show.
    it('keeps the row while a move is pending', async () => {
      pending.add('power-tailgate')
      const wrapper = mountCard({ hasPowerTailgate: true, isLocked: null })

      await wrapper.setProps({ trunkOpen: null })

      expect(buttonLabels(wrapper)).toHaveLength(1)
      expect(wrapper.text()).toContain(en.control.pending)
    })

    it('unlatches as before once switched off', () => {
      expect(buttonLabels(mountCard({ hasPowerTailgate: false, isLocked: null }))).toEqual(['Open'])
      expect(
        buttonLabels(mountCard({ hasPowerTailgate: false, isLocked: null, trunkOpen: true })),
      ).toEqual([])
    })

    it('offers nothing while the boot is not reported', () => {
      expect(
        buttonLabels(mountCard({ hasPowerTailgate: true, isLocked: null, trunkOpen: null })),
      ).toEqual([])
    })
  })
})
