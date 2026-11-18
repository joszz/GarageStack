import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import EditLayoutButton from '../EditLayoutButton.vue'

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

function button(editing: boolean) {
  return mount(EditLayoutButton, {
    props: { modelValue: editing },
    global: { plugins: [i18n], stubs: { FontAwesomeIcon: true } },
  })
}

describe('EditLayoutButton', () => {
  it('offers to edit the layout, and opens the editor', async () => {
    const wrapper = button(false)

    expect(wrapper.text()).toBe(en.dashboard.editLayout)
    expect(wrapper.classes()).toContain('btn-outline-secondary')
    await wrapper.trigger('click')

    expect(wrapper.emitted('update:modelValue')).toEqual([[true]])
  })

  it('says Done while editing, and closes the editor', async () => {
    const wrapper = button(true)

    expect(wrapper.text()).toBe(en.dashboard.doneEditing)
    expect(wrapper.classes()).toContain('btn-primary')
    await wrapper.trigger('click')

    expect(wrapper.emitted('update:modelValue')).toEqual([[false]])
  })
})
