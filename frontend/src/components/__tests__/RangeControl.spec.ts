import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import RangeControl from '../RangeControl.vue'

function mountRange(props: Record<string, unknown> = {}) {
  return mount(RangeControl, {
    props: {
      icon: 'couch',
      label: 'Driver seat',
      valueText: 'Low',
      min: 0,
      max: 3,
      modelValue: 1,
      ...props,
    },
    global: { stubs: { FontAwesomeIcon: true } },
  })
}

describe('RangeControl', () => {
  it('shows the label and the current value text, and labels the slider for screen readers', () => {
    const wrapper = mountRange()
    const input = wrapper.find('input[type="range"]')

    expect(wrapper.find('.range-control__label').text()).toBe('Driver seat')
    expect(wrapper.find('.range-control__value').text()).toBe('Low')
    expect(input.attributes('aria-label')).toBe('Driver seat')
    expect(input.attributes('aria-valuetext')).toBe('Low')
    expect(input.attributes('min')).toBe('0')
    expect(input.attributes('max')).toBe('3')
  })

  it('emits the new value as a number while dragging', async () => {
    const wrapper = mountRange()

    await wrapper.find('input[type="range"]').setValue('3')

    expect(wrapper.emitted('update:modelValue')).toEqual([[3]])
  })

  it('shows edge labels around the slider and tick labels under it only when given', () => {
    const plain = mountRange()
    expect(plain.find('.range-control__row').findAll('span')).toHaveLength(0)
    expect(plain.find('.range-control__labels').exists()).toBe(false)

    const labelled = mountRange({
      edgeLabels: ['16°', '28°'],
      tickLabels: ['Off', 'Low', 'Medium', 'High'],
    })
    expect(labelled.find('.range-control__row').text()).toBe('16°28°')
    expect(labelled.findAll('.range-control__labels span').map((s) => s.text())).toEqual([
      'Off',
      'Low',
      'Medium',
      'High',
    ])
  })
})
