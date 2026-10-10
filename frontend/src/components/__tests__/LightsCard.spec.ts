import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import LightsCard from '../LightsCard.vue'

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

function mountCard(props: Record<string, unknown> = {}) {
  return mount(LightsCard, {
    props: { mainBeam: false, dippedBeam: false, side: false, ...props },
    global: { plugins: [i18n], stubs: { DetailModal: true, FontAwesomeIcon: true } },
  })
}

describe('LightsCard', () => {
  it('shows lights that are off as a plain tile, not as a problem', () => {
    const card = mountCard().find('.status-card')

    expect(card.find('.status-card__value').text()).toBe('Off')
    expect(card.classes()).not.toContain('status-card--danger')
    expect(card.classes()).not.toContain('status-card--success')
  })

  it('names the lights that are on', () => {
    const card = mountCard({ dippedBeam: true, side: true }).find('.status-card')

    expect(card.find('.status-card__value').text()).toBe('Low beam · Sidelights')
    expect(card.classes()).toContain('status-card--success')
  })
})
