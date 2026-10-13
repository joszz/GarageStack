import { describe, it, expect, afterEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import CommandButton from '../CommandButton.vue'

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

function mountButton(props: Record<string, unknown> = {}) {
  return mount(CommandButton, {
    props: { pending: false, sending: false, icon: 'car-rear', label: 'Open', ...props },
    global: { plugins: [i18n], stubs: { FontAwesomeIcon: true } },
  })
}

afterEach(() => vi.useRealTimers())

describe('CommandButton', () => {
  it('sends at one tap', async () => {
    const wrapper = mountButton()

    await wrapper.trigger('click')

    expect(wrapper.emitted('click')).toHaveLength(1)
  })

  it('asks for a second tap when it has a confirm label', async () => {
    const wrapper = mountButton({ confirmLabel: 'Tap again to open' })

    await wrapper.trigger('click')
    expect(wrapper.emitted('click')).toBeUndefined()
    expect(wrapper.text()).toBe('Tap again to open')
    expect(wrapper.classes()).toContain('btn--armed')

    await wrapper.trigger('click')
    expect(wrapper.emitted('click')).toHaveLength(1)
    expect(wrapper.text()).toBe('Open')
  })

  it('forgets the first tap after a few seconds', async () => {
    vi.useFakeTimers()
    const wrapper = mountButton({ confirmLabel: 'Tap again to open' })

    await wrapper.trigger('click')
    vi.advanceTimersByTime(4000)
    await wrapper.vm.$nextTick()
    expect(wrapper.text()).toBe('Open')

    await wrapper.trigger('click')
    expect(wrapper.emitted('click')).toBeUndefined()
  })

  it('forgets the first tap when focus moves on', async () => {
    const wrapper = mountButton({ confirmLabel: 'Tap again to open' })

    await wrapper.trigger('click')
    await wrapper.trigger('blur')

    expect(wrapper.text()).toBe('Open')
  })

  it('says it is waiting for the car while pending', () => {
    expect(mountButton({ pending: true }).text()).toBe(en.control.pending)
  })
})
