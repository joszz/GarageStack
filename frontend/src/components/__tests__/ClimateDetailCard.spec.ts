import { describe, it, expect, vi, beforeEach } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import { ref } from 'vue'
import en from '@/locales/en.json'
import ClimateDetailCard from '../ClimateDetailCard.vue'

const send = vi.fn<(...args: unknown[]) => Promise<boolean>>().mockResolvedValue(true)

vi.mock('@/composables/useVehicleCommand', () => ({
  useVehicleCommand: () => ({
    sending: ref(null),
    lastResult: ref(null),
    isPending: () => false,
    send,
    waitUntilSettled: vi.fn<() => Promise<void>>().mockResolvedValue(undefined),
  }),
}))

const DetailModalStub = {
  name: 'DetailModal',
  template: '<div v-if="open"><slot /><slot name="footer" /></div>',
  props: ['open', 'title'],
  emits: ['close'],
}

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

function mountCard(props: Record<string, unknown> = {}) {
  return mount(ClimateDetailCard, {
    props: {
      vin: 'FAKEVN00000000001',
      climateOn: true,
      climateMode: 'on',
      remoteTemperature: 21,
      interiorTemperature: 19,
      exteriorTemperature: 4,
      heatedSeatFrontLeft: 0,
      heatedSeatFrontRight: 0,
      rearWindowDefroster: false,
      ...props,
    },
    global: {
      plugins: [i18n],
      stubs: { DetailModal: DetailModalStub, FontAwesomeIcon: true },
    },
  })
}

async function open(wrapper: ReturnType<typeof mountCard>) {
  await wrapper.find('.status-card').trigger('click')
}

function button(wrapper: ReturnType<typeof mountCard>, label: string) {
  return wrapper.findAll('button').find((b) => b.text() === label)!
}

describe('ClimateDetailCard', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    send.mockClear()
  })

  it('names a running climate mode other than normal climate on the card', () => {
    const wrapper = mountCard({ climateMode: 'blowingonly' })

    expect(wrapper.find('.status-card__value').text()).toContain('Fan only')
  })

  it('starts the mode chips on the running mode', async () => {
    const wrapper = mountCard({ climateMode: 'front' })
    await open(wrapper)

    expect(button(wrapper, 'Front defrost').attributes('aria-pressed')).toBe('true')
  })

  it('sends the chosen mode, and no temperature, when switching to fan only', async () => {
    const wrapper = mountCard()
    await open(wrapper)

    await button(wrapper, 'Fan only').trigger('click')
    expect(wrapper.find('input[type="range"]').attributes('disabled')).toBeDefined()

    await button(wrapper, 'Apply').trigger('click')
    await flushPromises()

    expect(send).toHaveBeenCalledTimes(1)
    expect(send.mock.calls[0]!.slice(0, 3)).toEqual(['FAKEVN00000000001', 'climate', 'blowingonly'])
  })

  it('hides the mode chips and sends off when climate is switched off', async () => {
    const wrapper = mountCard({ climateMode: 'front' })
    await open(wrapper)

    await wrapper.find('input[role="switch"]').trigger('change')
    expect(wrapper.find('.toggle-chip-group').exists()).toBe(false)

    await button(wrapper, 'Apply').trigger('click')
    await flushPromises()

    expect(send.mock.calls[0]!.slice(0, 3)).toEqual(['FAKEVN00000000001', 'climate', 'off'])
  })

  it('confirms a mode change only once the car reports that mode', async () => {
    const wrapper = mountCard()
    await open(wrapper)

    await button(wrapper, 'Front defrost').trigger('click')
    await button(wrapper, 'Apply').trigger('click')
    await flushPromises()

    const { isConfirmed } = send.mock.calls[0]![3] as {
      isConfirmed: (s: { climateOn: boolean | null; climateMode: string | null }) => boolean
    }
    expect(isConfirmed({ climateOn: true, climateMode: 'on' })).toBe(false)
    expect(isConfirmed({ climateOn: true, climateMode: 'front' })).toBe(true)
  })
})
