import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h } from 'vue'
import { enableAutoUnmount, mount } from '@vue/test-utils'
import { LONG_PRESS_MS, LONG_PRESS_TOLERANCE_PX, useLongPress } from '../useLongPress'

enableAutoUnmount(afterEach)

// A grid with one card in it, its long press counted.
function grid() {
  const onLongPress = vi.fn<() => void>()
  const wrapper = mount(
    defineComponent({
      setup() {
        const bindings = useLongPress(onLongPress)
        return () => h('div', { class: 'grid', ...bindings }, [h('div', { class: 'card' })])
      },
    }),
    { attachTo: document.body },
  )
  return { onLongPress, grid: wrapper.find('.grid'), card: wrapper.find('.card') }
}

function pointer(type: string, init: PointerEventInit = {}) {
  return new PointerEvent(type, {
    bubbles: true,
    cancelable: true,
    isPrimary: true,
    pointerType: 'touch',
    ...init,
  })
}

describe('useLongPress', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => vi.useRealTimers())

  it('fires when a finger rests on a card long enough', () => {
    const { onLongPress, card } = grid()

    card.element.dispatchEvent(pointer('pointerdown'))
    vi.advanceTimersByTime(LONG_PRESS_MS - 1)
    expect(onLongPress).not.toHaveBeenCalled()
    vi.advanceTimersByTime(1)

    expect(onLongPress).toHaveBeenCalledOnce()
  })

  it('does not fire for a tap', () => {
    const { onLongPress, card } = grid()

    card.element.dispatchEvent(pointer('pointerdown'))
    card.element.dispatchEvent(pointer('pointerup'))
    vi.advanceTimersByTime(LONG_PRESS_MS)

    expect(onLongPress).not.toHaveBeenCalled()
  })

  it('does not fire for a mouse or a pen, which have the edit button', () => {
    const { onLongPress, card } = grid()

    card.element.dispatchEvent(pointer('pointerdown', { pointerType: 'mouse' }))
    vi.advanceTimersByTime(LONG_PRESS_MS)
    card.element.dispatchEvent(pointer('pointerdown', { pointerType: 'pen' }))
    vi.advanceTimersByTime(LONG_PRESS_MS)

    expect(onLongPress).not.toHaveBeenCalled()
  })

  it('does not fire on the gap between the cards', () => {
    const { onLongPress, grid: el } = grid()

    el.element.dispatchEvent(pointer('pointerdown'))
    vi.advanceTimersByTime(LONG_PRESS_MS)

    expect(onLongPress).not.toHaveBeenCalled()
  })

  it('lets a finger drift a little, but a scroll cancels', () => {
    const { onLongPress, card } = grid()

    card.element.dispatchEvent(pointer('pointerdown', { clientX: 100, clientY: 100 }))
    card.element.dispatchEvent(
      pointer('pointermove', { clientX: 100 + LONG_PRESS_TOLERANCE_PX, clientY: 100 }),
    )
    vi.advanceTimersByTime(LONG_PRESS_MS)
    expect(onLongPress).toHaveBeenCalledOnce()

    card.element.dispatchEvent(pointer('pointerdown', { clientX: 100, clientY: 100 }))
    card.element.dispatchEvent(pointer('pointermove', { clientX: 100, clientY: 130 }))
    vi.advanceTimersByTime(LONG_PRESS_MS)
    expect(onLongPress).toHaveBeenCalledOnce()
  })

  it('stops when the browser takes the touch over to scroll', () => {
    const { onLongPress, card } = grid()

    card.element.dispatchEvent(pointer('pointerdown'))
    card.element.dispatchEvent(pointer('pointercancel'))
    vi.advanceTimersByTime(LONG_PRESS_MS)

    expect(onLongPress).not.toHaveBeenCalled()
  })

  it("takes Android's own long press, the context menu, as the long press and keeps the menu shut", () => {
    const { onLongPress, card } = grid()

    card.element.dispatchEvent(pointer('pointerdown'))
    const menu = new MouseEvent('contextmenu', { bubbles: true, cancelable: true })
    card.element.dispatchEvent(menu)

    expect(menu.defaultPrevented).toBe(true)
    expect(onLongPress).toHaveBeenCalledOnce()
    vi.advanceTimersByTime(LONG_PRESS_MS)
    expect(onLongPress).toHaveBeenCalledOnce()
  })

  it('leaves the context menu of a right click alone', () => {
    const { onLongPress, card } = grid()

    const menu = new MouseEvent('contextmenu', { bubbles: true, cancelable: true })
    card.element.dispatchEvent(menu)

    expect(menu.defaultPrevented).toBe(false)
    expect(onLongPress).not.toHaveBeenCalled()
  })

  it('swallows the click of the finger that made the long press, and only that one', () => {
    const { card } = grid()
    const opened = vi.fn<() => void>()
    card.element.addEventListener('click', opened)

    card.element.dispatchEvent(pointer('pointerdown'))
    vi.advanceTimersByTime(LONG_PRESS_MS)
    card.element.dispatchEvent(pointer('pointerup'))
    card.element.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }))
    expect(opened).not.toHaveBeenCalled()

    vi.advanceTimersByTime(0)
    card.element.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }))
    expect(opened).toHaveBeenCalledOnce()
  })
})
