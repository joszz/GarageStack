import { afterEach, describe, it, expect } from 'vitest'
import { defineComponent, h, nextTick, ref } from 'vue'
import { mount, type VueWrapper } from '@vue/test-utils'
import { useDialogFocus } from '../useDialogFocus'

const mounted: VueWrapper[] = []

// A dialog with two buttons, shown while `open` is true, the way DetailModal renders one.
function mountDialog(open = ref(false), name = 'dialog') {
  const wrapper = mount(
    defineComponent({
      setup() {
        const dialog = ref<HTMLElement | null>(null)
        useDialogFocus(dialog, () => open.value)
        return () =>
          open.value
            ? h('div', { ref: dialog, tabindex: -1, 'data-name': name }, [
                h('button', { class: 'first' }, 'first'),
                h('button', { class: 'last' }, 'last'),
              ])
            : null
      },
    }),
    { attachTo: document.body },
  )
  mounted.push(wrapper)
  return { wrapper, open }
}

function opener(): HTMLButtonElement {
  const button = document.createElement('button')
  document.body.append(button)
  button.focus()
  return button
}

function pressTab(shiftKey = false) {
  const event = new KeyboardEvent('keydown', { key: 'Tab', shiftKey, cancelable: true })
  document.dispatchEvent(event)
  return event
}

function focused(): string | null {
  const active = document.activeElement as HTMLElement | null
  return active?.className || active?.dataset.name || null
}

afterEach(() => {
  mounted.splice(0).forEach((wrapper) => wrapper.unmount())
  document.body.innerHTML = ''
})

describe('useDialogFocus', () => {
  it('moves focus into the dialog when it opens, and back to the opener when it closes', async () => {
    const button = opener()
    const { open } = mountDialog()

    open.value = true
    await nextTick()
    expect(focused()).toBe('dialog')

    open.value = false
    await nextTick()
    expect(document.activeElement).toBe(button)
  })

  it('takes focus for a dialog that is open from the start', async () => {
    opener()
    mountDialog(ref(true))
    await nextTick()

    expect(focused()).toBe('dialog')
  })

  it('cycles Tab and Shift+Tab inside the dialog', async () => {
    const { open } = mountDialog()
    open.value = true
    await nextTick()

    expect(pressTab(true).defaultPrevented).toBe(true)
    expect(focused()).toBe('last')

    expect(pressTab().defaultPrevented).toBe(true)
    expect(focused()).toBe('first')

    expect(pressTab().defaultPrevented).toBe(false)
  })

  it('leaves Tab to the innermost of two open dialogs', async () => {
    const outer = mountDialog(ref(false), 'outer')
    outer.open.value = true
    await nextTick()
    const inner = mountDialog(ref(false), 'inner')
    inner.open.value = true
    await nextTick()
    const innerLast = inner.wrapper.find('.last').element as HTMLElement
    innerLast.focus()

    pressTab()

    expect(inner.wrapper.find('.first').element).toBe(document.activeElement)
  })

  it('ignores Tab while closed', () => {
    mountDialog()

    expect(pressTab().defaultPrevented).toBe(false)
  })
})
