import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { ref } from 'vue'
import en from '@/locales/en.json'
import FindMyCarCard from '../FindMyCarCard.vue'

vi.mock('@/composables/useVehicleCommand', () => ({
  useVehicleCommand: () => ({
    sending: ref(null),
    lastResult: ref(null),
    isPending: () => false,
    send: vi.fn<(...args: unknown[]) => Promise<boolean>>().mockResolvedValue(true),
  }),
}))

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

describe('FindMyCarCard', () => {
  it('reads Off while the horn is quiet, rather than a dash that looks like missing data', () => {
    const card = mount(FindMyCarCard, {
      props: { vin: 'FAKEVN00000000001' },
      global: { plugins: [i18n], stubs: { DetailModal: true, FontAwesomeIcon: true } },
    }).find('.status-card')

    expect(card.find('.status-card__value').text()).toBe('Off')
    expect(card.classes()).not.toContain('status-card--warning')
  })
})
