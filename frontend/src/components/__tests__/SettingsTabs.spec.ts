import { afterEach, describe, expect, it } from 'vitest'
import { enableAutoUnmount, mount } from '@vue/test-utils'
import SettingsTabs from '../SettingsTabs.vue'

enableAutoUnmount(afterEach)

const tab = (id: string, shortLabel?: string) => ({
  id,
  icon: 'circle',
  label: `Tab ${id}`,
  shortLabel,
})

function mountTabs(...tabs: ReturnType<typeof tab>[]) {
  const wrapper = mount(SettingsTabs, {
    props: {
      tabs,
      label: 'Groups',
      idPrefix: 'test',
      modelValue: tabs[0]!.id,
      'onUpdate:modelValue': (value: string) => wrapper.setProps({ modelValue: value }),
    },
    attachTo: document.body,
    global: { stubs: { FontAwesomeIcon: true } },
  })
  return wrapper
}

describe('SettingsTabs', () => {
  it('names each tab in full and shows the short name where there is one', () => {
    const wrapper = mountTabs(tab('one', 'One'), tab('two'))
    const buttons = wrapper.findAll('[role=tab]')

    expect(wrapper.get('[role=tablist]').attributes('aria-label')).toBe('Groups')
    expect(buttons.map((button) => button.text())).toEqual(['One', 'Tab two'])
    expect(buttons.map((button) => button.attributes('aria-label'))).toEqual(['Tab one', 'Tab two'])
    expect(buttons[0]!.attributes('id')).toBe('test-tab-one')
    expect(buttons[0]!.attributes('aria-controls')).toBe('test-panel-one')
  })

  it('marks a row of three or fewer, which keeps the chosen name on narrower phones', () => {
    const few = mountTabs(tab('one'), tab('two'), tab('three'))
    const many = mountTabs(tab('one'), tab('two'), tab('three'), tab('four'))

    expect(few.get('[role=tablist]').classes()).toContain('settings-tabs--few')
    expect(many.get('[role=tablist]').classes()).not.toContain('settings-tabs--few')
  })

  it('selects with a click and moves focus with the arrow keys', async () => {
    const wrapper = mountTabs(tab('one'), tab('two'), tab('three'))
    const button = (id: string) => wrapper.get(`[data-tab="${id}"]`)

    await button('two').trigger('click')
    expect(wrapper.emitted('update:modelValue')).toEqual([['two']])

    await button('two').trigger('keydown', { key: 'ArrowRight' })
    expect(document.activeElement).toBe(button('three').element)
    expect(button('three').attributes('aria-selected')).toBe('true')
    expect(button('three').attributes('tabindex')).toBe('0')
    expect(button('two').attributes('tabindex')).toBe('-1')
  })
})
