import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h } from 'vue'
import { enableAutoUnmount, mount } from '@vue/test-utils'
import {
  LIFT_CLICK_MS,
  LONG_PRESS_EXEMPT_CLASS,
  LONG_PRESS_MS,
  LONG_PRESS_TOLERANCE_PX,
  SCROLL_SETTLE_MS,
  useLongPress,
} from '../useLongPress'

enableAutoUnmount(afterEach)

// A grid with one card in it, its long press counted. Part of the card is a chart or an address,
// where holding a finger means something else.
function grid() {
  const onLongPress = vi.fn<() => void>()
  const onPressStart = vi.fn<() => void>()
  const wrapper = mount(
    defineComponent({
      setup() {
        const bindings = useLongPress(onLongPress, { onPressStart })
        return () =>
          h('div', { class: 'grid', ...bindings }, [
            h('div', { class: 'card' }, [h('div', { class: ['plot', LONG_PRESS_EXEMPT_CLASS] })]),
          ])
      },
    }),
    { attachTo: document.body },
  )
  return {
    onLongPress,
    onPressStart,
    grid: wrapper.find('.grid'),
    card: wrapper.find('.card'),
    exempt: wrapper.find('.plot'),
  }
}

const click = () => new MouseEvent('click', { bubbles: true, cancelable: true })

// An event as if it happened at `ms` on the page's clock, which a test cannot otherwise set.
function at<E extends Event>(event: E, ms: number): E {
  Object.defineProperty(event, 'timeStamp', { value: ms })
  return event
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

  it('says a press began as the finger goes down, to get ready while it rests', () => {
    const { onPressStart, card, grid: el, exempt } = grid()

    card.element.dispatchEvent(pointer('pointerdown', { pointerType: 'mouse' }))
    el.element.dispatchEvent(pointer('pointerdown'))
    exempt.element.dispatchEvent(pointer('pointerdown'))
    expect(onPressStart).not.toHaveBeenCalled()

    card.element.dispatchEvent(pointer('pointerdown'))
    expect(onPressStart).toHaveBeenCalledOnce()
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

  it('swallows the click the lifting finger may still bring, even a little late', () => {
    const { card } = grid()
    const opened = vi.fn<() => void>()
    card.element.addEventListener('click', opened)

    card.element.dispatchEvent(pointer('pointerdown'))
    vi.advanceTimersByTime(LONG_PRESS_MS)
    card.element.dispatchEvent(pointer('pointerup'))
    vi.advanceTimersByTime(LIFT_CLICK_MS - 1)
    card.element.dispatchEvent(click())
    expect(opened).not.toHaveBeenCalled()

    vi.advanceTimersByTime(1)
    card.element.dispatchEvent(click())
    expect(opened).toHaveBeenCalledOnce()
  })

  // WebKit keeps sending a touch's events to the element it began on, removed by edit mode.
  it('hears the lift on the pressed card once edit mode took it off the page', () => {
    const { card } = grid()
    const elsewhere = document.body.appendChild(document.createElement('button'))
    const opened = vi.fn<() => void>()
    elsewhere.addEventListener('click', opened)

    card.element.dispatchEvent(pointer('pointerdown'))
    vi.advanceTimersByTime(LONG_PRESS_MS)
    const pressed = card.element
    pressed.remove()
    pressed.dispatchEvent(pointer('pointerup'))
    vi.advanceTimersByTime(LIFT_CLICK_MS)

    elsewhere.dispatchEvent(click())
    expect(opened).toHaveBeenCalledOnce()
    elsewhere.remove()
  })

  it('lets the next tap through even when the lift was never heard', () => {
    const { card } = grid()
    const done = document.body.appendChild(document.createElement('button'))
    const opened = vi.fn<() => void>()
    done.addEventListener('click', opened)

    card.element.dispatchEvent(pointer('pointerdown'))
    vi.advanceTimersByTime(LONG_PRESS_MS)
    done.dispatchEvent(pointer('pointerdown'))
    done.dispatchEvent(pointer('pointerup'))
    done.dispatchEvent(click())

    expect(opened).toHaveBeenCalledOnce()
    done.remove()
  })

  it('leaves alone a press on a part where holding means something else', () => {
    const { onLongPress, exempt } = grid()

    exempt.element.dispatchEvent(pointer('pointerdown'))
    vi.advanceTimersByTime(LONG_PRESS_MS)
    const menu = new MouseEvent('contextmenu', { bubbles: true, cancelable: true })
    exempt.element.dispatchEvent(menu)

    expect(onLongPress).not.toHaveBeenCalled()
    expect(menu.defaultPrevented).toBe(false)
  })

  it('does not fire for a finger that only stopped a fling and stays to read', () => {
    const { onLongPress, card } = grid()

    window.dispatchEvent(at(new Event('scroll'), 1000))
    card.element.dispatchEvent(at(pointer('pointerdown'), 1000 + SCROLL_SETTLE_MS - 1))
    vi.advanceTimersByTime(LONG_PRESS_MS)
    expect(onLongPress).not.toHaveBeenCalled()

    card.element.dispatchEvent(at(pointer('pointerdown'), 1000 + SCROLL_SETTLE_MS))
    vi.advanceTimersByTime(LONG_PRESS_MS)
    expect(onLongPress).toHaveBeenCalledOnce()
  })

  it('stops when the page scrolls under the finger', () => {
    const { onLongPress, card } = grid()

    card.element.dispatchEvent(pointer('pointerdown'))
    document.body.dispatchEvent(new Event('scroll'))
    vi.advanceTimersByTime(LONG_PRESS_MS)

    expect(onLongPress).not.toHaveBeenCalled()
  })

  describe('the haptic tick', () => {
    const vibrate = vi.fn<(pattern: VibratePattern) => boolean>()

    function activation(hasBeenActive: boolean) {
      Object.defineProperty(navigator, 'userActivation', {
        value: { hasBeenActive, isActive: false },
        configurable: true,
      })
    }

    beforeEach(() => {
      vibrate.mockClear()
      Object.defineProperty(navigator, 'vibrate', { value: vibrate, configurable: true })
    })

    afterEach(() => {
      Reflect.deleteProperty(navigator, 'vibrate')
      Reflect.deleteProperty(navigator, 'userActivation')
    })

    it('ticks once the page has been tapped', () => {
      activation(true)
      const { card } = grid()

      card.element.dispatchEvent(pointer('pointerdown'))
      vi.advanceTimersByTime(LONG_PRESS_MS)

      expect(vibrate).toHaveBeenCalledOnce()
    })

    it('stays still before the first tap, which Chrome would refuse and log', () => {
      activation(false)
      const { onLongPress, card } = grid()

      card.element.dispatchEvent(pointer('pointerdown'))
      vi.advanceTimersByTime(LONG_PRESS_MS)

      expect(onLongPress).toHaveBeenCalledOnce()
      expect(vibrate).not.toHaveBeenCalled()
    })
  })
})
