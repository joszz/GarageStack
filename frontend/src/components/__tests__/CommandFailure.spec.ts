import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import CommandFailure from '../CommandFailure.vue'

const i18n = createI18n({
  legacy: false,
  locale: 'en',
  messages: {
    en: {
      control: {
        error: 'Command failed',
        rejected: 'The car did not carry out the command',
      },
    },
  },
})

function mountFailure(detail: string | null) {
  return mount(CommandFailure, { props: { detail }, global: { plugins: [i18n] } })
}

describe('CommandFailure', () => {
  it('says the request failed when there is no reason from the car', () => {
    const wrapper = mountFailure(null)

    expect(wrapper.text()).toBe('Command failed')
    expect(wrapper.find('.command-failure__detail').exists()).toBe(false)
  })

  it("says the car refused and adds the gateway's reason", () => {
    const wrapper = mountFailure('vehicle is not online')

    expect(wrapper.text()).toContain('The car did not carry out the command')
    expect(wrapper.find('.command-failure__detail').text()).toBe('vehicle is not online')
  })

  it('announces itself to screen readers', () => {
    expect(mountFailure(null).attributes('role')).toBe('alert')
  })
})
