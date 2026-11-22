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
    await bar.get('.btn-primary').trigger('click')

    expect(bar.get('.btn-primary').text()).toBe(en.dashboard.doneEditing)
    expect(wrapper.emitted('update:modelValue')).toEqual([[false]])
  })

  it('offers no reset outside the editor', () => {
    expect(button(false).findAll('button')).toHaveLength(1)
  })

  it('resets the layout from beside Done in the header, which gives way to the bar on touch', async () => {
    const wrapper = button(true)
    const header = wrapper.findAll('button')
    const reset = header[0]!

    expect(header[1]!.classes()).toContain('edit-layout-btn')
    expect(reset.text()).toBe(en.dashboard.resetLayoutShort)
    expect(reset.attributes('title')).toBe(en.dashboard.resetLayout)
    expect(reset.classes()).toContain('edit-layout-btn--touch-hidden')
    await reset.trigger('click')

    expect(wrapper.emitted('reset')).toEqual([[]])
    expect(wrapper.emitted('update:modelValue')).toBeUndefined()
  })

  it('resets the layout from the floating bar, and stays in the editor', async () => {
    const wrapper = button(true)
    const reset = wrapper.get('.edit-mode-bar .btn-outline-secondary')

    expect(reset.text()).toBe(en.dashboard.resetLayoutShort)
    await reset.trigger('click')

    expect(wrapper.emitted('reset')).toEqual([[]])
    expect(wrapper.emitted('update:modelValue')).toBeUndefined()
  })
})
