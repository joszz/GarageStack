import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import EditLayoutButton from '../EditLayoutButton.vue'

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

function button(editing: boolean, cardsShown = true) {
  return mount(EditLayoutButton, {
    props: { modelValue: editing, cardsShown },
    global: { plugins: [i18n], stubs: { FontAwesomeIcon: true, teleport: true } },
  })
}

describe('EditLayoutButton', () => {
  it('offers to edit the layout, and opens the editor', async () => {
    const wrapper = button(false)
    const header = wrapper.get('.edit-layout-btn')

    expect(header.text()).toBe(en.dashboard.editLayout)
    expect(header.classes()).toContain('btn-outline-secondary')
    await header.trigger('click')

    expect(wrapper.emitted('update:modelValue')).toEqual([[true]])
  })

  it('says Done while editing, and closes the editor', async () => {
    const wrapper = button(true)
    const header = wrapper.get('.edit-layout-btn')

    expect(header.text()).toBe(en.dashboard.doneEditing)
    expect(header.classes()).toContain('btn-primary')
    await header.trigger('click')

    expect(wrapper.emitted('update:modelValue')).toEqual([[false]])
  })

  it('leaves opening to a long press on touch while a card is there to press', () => {
    expect(button(false, true).get('.edit-layout-btn').classes()).toContain(
      'edit-layout-btn--touch-hidden',
    )
  })

  it('keeps the button on touch when no card is there to press', () => {
    expect(button(false, false).get('.edit-layout-btn').classes()).not.toContain(
      'edit-layout-btn--touch-hidden',
    )
  })

  it('shows no floating bar outside the editor', () => {
    expect(button(false).find('.edit-mode-bar').exists()).toBe(false)
  })

  it('closes the editor from the floating bar, which takes the button over on touch', async () => {
    const wrapper = button(true, false)
    const bar = wrapper.get('.edit-mode-bar')

    expect(wrapper.get('.edit-layout-btn').classes()).toContain('edit-layout-btn--touch-hidden')
    expect(bar.attributes('aria-label')).toBe(en.dashboard.editingLayout)
    expect(bar.text()).toContain(en.dashboard.editingLayout)
    await bar.get('button').trigger('click')

    expect(wrapper.emitted('update:modelValue')).toEqual([[false]])
  })
})
