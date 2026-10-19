import { describe, it, expect } from 'vitest'
import { mount, type VueWrapper } from '@vue/test-utils'
import LicensePlateInput from '../LicensePlateInput.vue'

function mountInput(modelValue = '') {
  const wrapper: VueWrapper = mount(LicensePlateInput, {
    props: {
      label: 'Licence plate',
      modelValue,
      'onUpdate:modelValue': (value: string) => wrapper.setProps({ modelValue: value }),
    },
  })
  return wrapper
}

function field(wrapper: VueWrapper): HTMLInputElement {
  return wrapper.find('input').element as HTMLInputElement
}

// What the browser does on a keystroke: the field changes, the caret moves, an input event fires.
function edit(wrapper: VueWrapper, value: string, caret: number, inputType = 'insertText') {
  const input = field(wrapper)
  input.value = value
  input.setSelectionRange(caret, caret)
  input.dispatchEvent(new InputEvent('input', { inputType }))
}

function typeOneByOne(wrapper: VueWrapper, text: string) {
  for (const c of text) {
    const input = field(wrapper)
    const caret = input.selectionStart ?? input.value.length
    edit(wrapper, input.value.slice(0, caret) + c + input.value.slice(caret), caret + 1)
  }
}

describe('LicensePlateInput', () => {
  it('looks like a Dutch plate, with NL on the blue band', () => {
    const wrapper = mountInput()

    expect(wrapper.find('.nl-plate__band').text()).toBe('NL')
    expect(wrapper.findAll('.nl-plate__stars polygon')).toHaveLength(12)
    expect(field(wrapper).getAttribute('aria-label')).toBe('Licence plate')
  })

  it('places the dashes and capitals while typing', () => {
    const wrapper = mountInput()

    typeOneByOne(wrapper, 'hpk98b')

    expect(field(wrapper).value).toBe('HPK-98-B')
    const emitted = wrapper.emitted<[string]>('update:modelValue')!
    expect(emitted[emitted.length - 1]).toEqual(['HPK-98-B'])
    expect(field(wrapper).selectionStart).toBe(8)
  })

  it('splits a group of four into two pairs as the fourth character arrives', () => {
    const wrapper = mountInput()

    typeOneByOne(wrapper, 'AB123')
    const before = field(wrapper).value
    typeOneByOne(wrapper, '4')

    expect(before).toBe('AB-123')
    expect(field(wrapper).value).toBe('AB-12-34')
  })

  it('takes the character before a dash on backspace, not just the dash', () => {
    const wrapper = mountInput('AB-12-34')

    // The caret was after "AB-12-"; backspace took the dash.
    edit(wrapper, 'AB-1234', 5, 'deleteContentBackward')

    expect(field(wrapper).value).toBe('AB-134')
    expect(field(wrapper).selectionStart).toBe(4)
  })

  it('takes the character after a dash on delete, not just the dash', () => {
    const wrapper = mountInput('AB-12-34')

    // The caret was after "AB-12"; delete took the dash.
    edit(wrapper, 'AB-1234', 5, 'deleteContentForward')

    expect(field(wrapper).value).toBe('AB-124')
    expect(field(wrapper).selectionStart).toBe(5)
  })

  it('keeps the caret where it was when typing in the middle', () => {
    const wrapper = mountInput('B-12-CD')

    edit(wrapper, 'AB-12-CD', 1)

    expect(field(wrapper).value).toBe('AB-12-CD')
    expect(field(wrapper).selectionStart).toBe(1)
  })

  it('refuses anything but letters and digits, and more than eight', () => {
    const wrapper = mountInput('AB-12-CD')

    edit(wrapper, 'AB-12-CD.', 9)
    const afterSymbol = field(wrapper).value
    edit(wrapper, 'AB-12-CDEFG', 11)

    expect(afterSymbol).toBe('AB-12-CD')
    expect(field(wrapper).value).toBe('AB-12-CD-EF')
  })
})
