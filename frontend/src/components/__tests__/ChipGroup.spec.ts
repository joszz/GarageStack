import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import ChipGroup from '../ChipGroup.vue'

const OPTIONS = [
  { value: 'a', label: 'Alpha' },
  { value: 'b', label: 'Bravo', class: 'chip--b' },
  { value: 'c', label: 'Charlie' },
]

function mountGroup(modelValue: string | string[] | null, props: Record<string, unknown> = {}) {
  return mount(ChipGroup, {
    props: { options: OPTIONS, groupLabel: 'Pick', modelValue, ...props },
    global: { stubs: { FontAwesomeIcon: true } },
  })
}

function chip(wrapper: ReturnType<typeof mountGroup>, label: string) {
  return wrapper.findAll('button').find((b) => b.text() === label)!
}

describe('ChipGroup', () => {
  it('is a labelled group that marks the chosen chip as pressed', () => {
    const wrapper = mountGroup('b')

    expect(wrapper.attributes('role')).toBe('group')
    expect(wrapper.attributes('aria-label')).toBe('Pick')
    expect(chip(wrapper, 'Bravo').attributes('aria-pressed')).toBe('true')
    expect(chip(wrapper, 'Bravo').classes()).toEqual(
      expect.arrayContaining(['toggle-chip', 'toggle-chip--active', 'chip--b']),
    )
    expect(chip(wrapper, 'Alpha').attributes('aria-pressed')).toBe('false')
  })

  it('chooses one value at a time', async () => {
    const wrapper = mountGroup('a')

    await chip(wrapper, 'Charlie').trigger('click')

    expect(wrapper.emitted('update:modelValue')).toEqual([['c']])
  })

  it('keeps the choice when the chosen chip is pressed again', async () => {
    const wrapper = mountGroup('a')

    await chip(wrapper, 'Alpha').trigger('click')

    expect(wrapper.emitted('update:modelValue')).toBeUndefined()
  })

  it('clears the choice when deselectable and the chosen chip is pressed again', async () => {
    const wrapper = mountGroup('a', { deselectable: true })

    await chip(wrapper, 'Alpha').trigger('click')

    expect(wrapper.emitted('update:modelValue')).toEqual([[null]])
  })

  it('toggles values of an array model, kept in the options order', async () => {
    const wrapper = mountGroup(['c'])

    await chip(wrapper, 'Alpha').trigger('click')
    expect(wrapper.emitted('update:modelValue')?.[0]).toEqual([['a', 'c']])

    await wrapper.setProps({ modelValue: ['a', 'c'] })
    await chip(wrapper, 'Charlie').trigger('click')
    expect(wrapper.emitted('update:modelValue')?.[1]).toEqual([['a']])
  })

  it('disables every chip', () => {
    const wrapper = mountGroup(null, { disabled: true })

    expect(wrapper.findAll('button').every((b) => b.attributes('disabled') !== undefined)).toBe(
      true,
    )
  })
})
