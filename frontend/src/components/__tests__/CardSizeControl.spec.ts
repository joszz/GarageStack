import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import CardSizeControl from '../CardSizeControl.vue'

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

function mountControl(props: { canLarger: boolean; canSmaller: boolean }) {
  return mount(CardSizeControl, {
    props: { label: 'Overview', ...props },
    global: { plugins: [i18n], stubs: { FontAwesomeIcon: true } },
  })
}

describe('CardSizeControl', () => {
  it('is a group named for what it resizes, with a button for each way', () => {
    const wrapper = mountControl({ canLarger: true, canSmaller: true })

    expect(wrapper.get('[role="group"]').attributes('aria-label')).toBe('Overview')
    expect(wrapper.findAll('button').map((b) => b.attributes('aria-label'))).toEqual([
      en.dashboard.cardSmaller,
      en.dashboard.cardLarger,
    ])
  })

  it('says which way the finger asked, smaller or larger', async () => {
    const wrapper = mountControl({ canLarger: true, canSmaller: true })
    const [smaller, larger] = wrapper.findAll('button')

    await larger!.trigger('click')
    await smaller!.trigger('click')

    expect(Object.keys(wrapper.emitted())).toEqual(expect.arrayContaining(['larger', 'smaller']))
    expect(wrapper.emitted('larger')).toHaveLength(1)
    expect(wrapper.emitted('smaller')).toHaveLength(1)
  })

  it('dims a button that has nowhere further to go, keeps its focus, and asks for nothing', async () => {
    const wrapper = mountControl({ canLarger: false, canSmaller: true })
    const [smaller, larger] = wrapper.findAll('button')

    expect(larger!.attributes('aria-disabled')).toBe('true')
    expect(smaller!.attributes('aria-disabled')).toBe('false')
    await larger!.trigger('click')
    expect(wrapper.emitted('larger')).toBeUndefined()
  })
})
