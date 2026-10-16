import { describe, it, expect, vi } from 'vitest'
import { defineComponent, h } from 'vue'
import { mount } from '@vue/test-utils'
import { useEscapeKey } from '../useEscapeKey'

function mountWith(isOpen: () => boolean, onEscape: () => void) {
  return mount(
    defineComponent({
      setup() {
        useEscapeKey(isOpen, onEscape)
        return () => h('div')
      },
    }),
  )
}

function pressKey(key: string) {
  document.dispatchEvent(new KeyboardEvent('keydown', { key }))
}

describe('useEscapeKey', () => {
  it('closes an open dialog on Escape, and only on Escape', () => {
    const onEscape = vi.fn<() => void>()
    const wrapper = mountWith(() => true, onEscape)

    pressKey('Enter')
    pressKey('Escape')

    expect(onEscape).toHaveBeenCalledOnce()
    wrapper.unmount()
  })

  it('leaves a closed dialog alone', () => {
    const onEscape = vi.fn<() => void>()
    const wrapper = mountWith(() => false, onEscape)

    pressKey('Escape')

    expect(onEscape).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('stops listening once the component is gone', () => {
    const onEscape = vi.fn<() => void>()
    mountWith(() => true, onEscape).unmount()

    pressKey('Escape')

    expect(onEscape).not.toHaveBeenCalled()
  })
})
