import { afterEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h } from 'vue'
import { enableAutoUnmount, mount } from '@vue/test-utils'
import {
  EDGE_START_MAX_PX,
  EDGE_START_MIN_PX,
  SCROLL_INTENT_PX,
  SWIPE_DISTANCE_PX,
  SWIPE_DIRECTION_RATIO,
  useDrawerSwipe,
  useEdgeSwipeOffered,
} from '../useDrawerSwipe'

enableAutoUnmount(afterEach)

// Where most swipes here start: inside the band, clear of the edge the phone keeps for itself.
const X0 = EDGE_START_MIN_PX + 10

const state = { enabled: true, blocksOpening: false, open: false }

// A page with the drawer and its backdrop in it, the swipe's calls counted. The drawer is open
// when `state.open` says so, and a call to open or close flips it, as the real menu does.
function page() {
  const open = vi.fn<() => void>(() => (state.open = true))
  const close = vi.fn<() => void>(() => (state.open = false))
  const wrapper = mount(
    defineComponent({
      setup() {
        useDrawerSwipe({
          enabled: () => state.enabled,
          blocksOpening: () => state.blocksOpening,
          isOpen: () => state.open,
          open,
          close,
          closeFrom: '.sidebar, .sidebar-backdrop',
        })
        return () =>
          h('div', [
            h('nav', { class: 'sidebar' }, [h('a', { class: 'link' })]),
            h('div', { class: 'sidebar-backdrop' }),
            h('main', { class: 'content' }),
          ])
      },
    }),
    { attachTo: document.body },
  )
  return {
    wrapper,
    open,
    close,
    content: wrapper.get('.content').element,
    sidebar: wrapper.get('.sidebar').element,
    link: wrapper.get('.link').element,
    backdrop: wrapper.get('.sidebar-backdrop').element,
  }
}

/** A touch event's own report of the finger (jsdom has no Touch to build one from). */
function touch(type: string, target: Element, fingers: { clientX: number; clientY: number }[]) {
  const event = new Event(type, { bubbles: true, cancelable: true })
  Object.defineProperty(event, 'touches', { value: fingers })
  target.dispatchEvent(event)
}

function pointer(type: string, target: Element, init: PointerEventInit = {}) {
  target.dispatchEvent(
    new PointerEvent(type, {
      bubbles: true,
      cancelable: true,
      isPrimary: true,
      pointerType: 'touch',
      pointerId: 1,
      ...init,
    }),
  )
}

/** A finger down at (x, y) that moves to (x + dx, y + dy) in a few steps. */
function drag(
  target: Element,
  x: number,
  y: number,
  dx: number,
  dy = 0,
  init: PointerEventInit = {},
) {
  pointer('pointerdown', target, { clientX: x, clientY: y, ...init })
  for (const step of [0.25, 0.5, 0.75, 1])
    pointer('pointermove', target, { clientX: x + dx * step, clientY: y + dy * step, ...init })
}

afterEach(() => {
  state.enabled = true
  state.blocksOpening = false
  state.open = false
  document.body.innerHTML = ''
})

describe('useDrawerSwipe opening', () => {
  it('opens the menu for a swipe that starts at the left edge and goes right', () => {
    const { open, content } = page()

    drag(content, X0, 300, SWIPE_DISTANCE_PX)

    expect(open).toHaveBeenCalledOnce()
  })

  // The band is wide enough to hit with a thumb, and both of its ends count.
  it.each([EDGE_START_MIN_PX, (EDGE_START_MIN_PX + EDGE_START_MAX_PX) / 2, EDGE_START_MAX_PX])(
    'opens the menu for a swipe that starts %s px in from the edge',
    (x) => {
      const { open, content } = page()

      drag(content, x, 300, SWIPE_DISTANCE_PX)

      expect(open).toHaveBeenCalledOnce()
    },
  )

  // The phone's own back gesture takes the very edge, so the swipe never starts there: the two are
  // not mixed up, whichever the finger meant.
  it.each([0, 5, EDGE_START_MIN_PX - 1])(
    'leaves a swipe that starts %s px from the edge to the phone',
    (x) => {
      const { open, content } = page()

      drag(content, x, 300, SWIPE_DISTANCE_PX * 2)

      expect(open).not.toHaveBeenCalled()
    },
  )

  it.each([EDGE_START_MAX_PX + 1, 200])(
    'leaves a swipe that starts as far in as %s px to the page',
    (x) => {
      const { open, content } = page()

      drag(content, x, 300, SWIPE_DISTANCE_PX * 2)

      expect(open).not.toHaveBeenCalled()
    },
  )

  it('leaves a map, a chart or anything else that pans under the finger alone', () => {
    const { open, content } = page()
    for (const own of ['canvas', 'div.maplibregl-map', 'div.leaflet-container']) {
      const [tag, ...classes] = own.split('.')
      const element = document.createElement(tag!)
      element.classList.add(...classes)
      content.append(element)

      drag(element, X0, 300, SWIPE_DISTANCE_PX * 2)
    }

    expect(open).not.toHaveBeenCalled()
  })

  // A row of chips or a wide table scrolls sideways under the same finger, and gets it.
  it('leaves a row that scrolls sideways alone, and opens over one that does not', () => {
    const { open, content } = page()
    const row = document.createElement('div')
    row.style.overflowX = 'auto'
    content.append(row)

    Object.defineProperty(row, 'scrollWidth', { value: 600, configurable: true })
    Object.defineProperty(row, 'clientWidth', { value: 300, configurable: true })
    drag(row, X0, 300, SWIPE_DISTANCE_PX * 2)
    expect(open).not.toHaveBeenCalled()

    // The same row with nothing to scroll is just part of the page.
    Object.defineProperty(row, 'scrollWidth', { value: 300, configurable: true })
    drag(row, X0, 300, SWIPE_DISTANCE_PX * 2)
    expect(open).toHaveBeenCalledOnce()
  })

  it('opens over a row that overflows but does not scroll', () => {
    const { open, content } = page()
    const row = document.createElement('div')
    row.style.overflowX = 'hidden'
    Object.defineProperty(row, 'scrollWidth', { value: 600, configurable: true })
    Object.defineProperty(row, 'clientWidth', { value: 300, configurable: true })
    content.append(row)

    drag(row, X0, 300, SWIPE_DISTANCE_PX * 2)

    expect(open).toHaveBeenCalledOnce()
  })

  it('waits until the finger has gone far enough', () => {
    const { open, content } = page()

    drag(content, X0, 300, SWIPE_DISTANCE_PX - 1)

    expect(open).not.toHaveBeenCalled()
  })

  it('does not open for a swipe to the left', () => {
    const { open, content } = page()

    drag(content, X0, 300, -SWIPE_DISTANCE_PX * 2)

    expect(open).not.toHaveBeenCalled()
  })

  it('opens once, however far the finger goes on', () => {
    const { open, content } = page()

    drag(content, X0, 300, SWIPE_DISTANCE_PX * 3)
    pointer('pointermove', content, { clientX: 400, clientY: 300 })

    expect(open).toHaveBeenCalledOnce()
  })

  it('takes a finger that moves up or down first for a scroll, whatever it does next', () => {
    const { open, content } = page()

    pointer('pointerdown', content, { clientX: X0, clientY: 300 })
    pointer('pointermove', content, { clientX: X0 + 3, clientY: 300 + SCROLL_INTENT_PX + 1 })
    pointer('pointermove', content, { clientX: X0 + SWIPE_DISTANCE_PX * 2, clientY: 330 })

    expect(open).not.toHaveBeenCalled()
  })

  it('does not take a slanting swipe for one', () => {
    const { open, content } = page()
    const dx = SWIPE_DISTANCE_PX
    // Further sideways than up, but not by the margin a swipe needs.
    drag(content, X0, 300, dx, dx / SWIPE_DIRECTION_RATIO + 1)

    expect(open).not.toHaveBeenCalled()
  })

  it('still opens for a swipe that wanders a little up or down', () => {
    const { open, content } = page()

    drag(content, X0, 300, SWIPE_DISTANCE_PX * 2, SCROLL_INTENT_PX - 2)

    expect(open).toHaveBeenCalledOnce()
  })

  it('stops following a gesture the browser took over for good', () => {
    const { open, content } = page()

    pointer('pointerdown', content, { clientX: X0, clientY: 300 })
    touch('touchcancel', content, [])
    touch('touchmove', content, [{ clientX: X0 + SWIPE_DISTANCE_PX * 2, clientY: 300 }])

    expect(open).not.toHaveBeenCalled()
  })

  // Chrome on Android cancels the pointer a few pixels into a sideways drag, to pan the page, and
  // reports the rest of the drag as touch events only.
  describe('where the browser cancels the pointer and only the touch goes on', () => {
    it('opens the menu from the touch moves that follow', () => {
      const { open, content } = page()

      pointer('pointerdown', content, { clientX: X0, clientY: 300 })
      pointer('pointermove', content, { clientX: X0 + 6, clientY: 300 })
      pointer('pointercancel', content, { clientX: X0 + 6, clientY: 300 })
      touch('touchmove', content, [{ clientX: X0 + 20, clientY: 300 }])
      expect(open).not.toHaveBeenCalled()
      touch('touchmove', content, [{ clientX: X0 + SWIPE_DISTANCE_PX + 4, clientY: 300 }])

      expect(open).toHaveBeenCalledOnce()
    })

    it('closes the menu the same way', () => {
      state.open = true
      const { close, sidebar } = page()

      pointer('pointerdown', sidebar, { clientX: 200, clientY: 300 })
      pointer('pointercancel', sidebar, { clientX: 200, clientY: 300 })
      touch('touchmove', sidebar, [{ clientX: 200 - SWIPE_DISTANCE_PX - 4, clientY: 300 }])

      expect(close).toHaveBeenCalledOnce()
    })

    it('still takes a finger that goes up or down for a scroll', () => {
      const { open, content } = page()

      pointer('pointerdown', content, { clientX: X0, clientY: 300 })
      pointer('pointercancel', content, { clientX: X0, clientY: 300 })
      touch('touchmove', content, [{ clientX: X0 + 3, clientY: 300 + SCROLL_INTENT_PX + 1 }])
      touch('touchmove', content, [{ clientX: X0 + SWIPE_DISTANCE_PX * 2, clientY: 330 }])

      expect(open).not.toHaveBeenCalled()
    })

    it('gives up when a second finger lands', () => {
      const { open, content } = page()

      pointer('pointerdown', content, { clientX: X0, clientY: 300 })
      touch('touchmove', content, [
        { clientX: X0 + SWIPE_DISTANCE_PX * 2, clientY: 300 },
        { clientX: 200, clientY: 300 },
      ])
      touch('touchmove', content, [{ clientX: X0 + SWIPE_DISTANCE_PX * 2, clientY: 300 }])

      expect(open).not.toHaveBeenCalled()
    })

    it('does not start a swipe of its own: a touch that no pointerdown began is nothing', () => {
      const { open, content } = page()

      touch('touchmove', content, [{ clientX: X0 + SWIPE_DISTANCE_PX * 2, clientY: 300 }])

      expect(open).not.toHaveBeenCalled()
    })
  })

  it('forgets a finger that lifted before it went far enough', () => {
    const { open, content } = page()

    drag(content, X0, 300, 10)
    pointer('pointerup', content, { clientX: X0 + 10, clientY: 300 })
    pointer('pointermove', content, { clientX: X0 + SWIPE_DISTANCE_PX * 2, clientY: 300 })

    expect(open).not.toHaveBeenCalled()
  })

  it('ignores a second finger, and follows only the one that landed first', () => {
    const { open, content } = page()

    pointer('pointerdown', content, { clientX: X0, clientY: 300, pointerId: 1 })
    pointer('pointermove', content, {
      clientX: X0 + SWIPE_DISTANCE_PX * 2,
      clientY: 300,
      pointerId: 2,
      isPrimary: false,
    })
    expect(open).not.toHaveBeenCalled()

    pointer('pointermove', content, { clientX: X0 + SWIPE_DISTANCE_PX, clientY: 300, pointerId: 1 })
    expect(open).toHaveBeenCalledOnce()
  })

  it.each(['mouse', 'pen'])('ignores a %s, which has no thumb to swipe with', (pointerType) => {
    const { open, content } = page()

    drag(content, X0, 300, SWIPE_DISTANCE_PX * 2, 0, { pointerType })

    expect(open).not.toHaveBeenCalled()
  })

  it('does nothing while swiping is off', () => {
    state.enabled = false
    const { open, close, content, sidebar } = page()

    drag(content, X0, 300, SWIPE_DISTANCE_PX * 2)
    state.open = true
    drag(sidebar, 200, 300, -SWIPE_DISTANCE_PX * 2)

    expect(open).not.toHaveBeenCalled()
    expect(close).not.toHaveBeenCalled()
  })

  // A map pans on any touch, so on its page the edge swipe would fight it.
  it('does not open on a page that blocks it', () => {
    state.blocksOpening = true
    const { open, content } = page()

    drag(content, X0, 300, SWIPE_DISTANCE_PX * 2)

    expect(open).not.toHaveBeenCalled()
  })

  it('does not open the menu under a dialog', () => {
    const { open, content } = page()
    const dialog = document.createElement('div')
    dialog.setAttribute('role', 'dialog')
    dialog.setAttribute('aria-modal', 'true')
    document.body.append(dialog)

    drag(content, X0, 300, SWIPE_DISTANCE_PX * 2)

    expect(open).not.toHaveBeenCalled()
  })

  it('stops listening once the app is gone', () => {
    const { open, wrapper } = page()
    wrapper.unmount()

    drag(document.body, X0, 300, SWIPE_DISTANCE_PX * 2)

    expect(open).not.toHaveBeenCalled()
  })
})

describe('useDrawerSwipe closing', () => {
  it('closes the open menu for a swipe to the left that starts on it', () => {
    state.open = true
    const { close, link } = page()

    drag(link, 200, 300, -SWIPE_DISTANCE_PX)

    expect(close).toHaveBeenCalledOnce()
  })

  it('closes it for a swipe that starts on the backdrop', () => {
    state.open = true
    const { close, backdrop } = page()

    drag(backdrop, 300, 300, -SWIPE_DISTANCE_PX)

    expect(close).toHaveBeenCalledOnce()
  })

  it('does not close it for a swipe that starts on the page behind', () => {
    state.open = true
    const { close, content } = page()

    drag(content, 300, 300, -SWIPE_DISTANCE_PX * 2)

    expect(close).not.toHaveBeenCalled()
  })

  it('does not close it for a swipe to the right', () => {
    state.open = true
    const { close, sidebar } = page()

    drag(sidebar, 50, 300, SWIPE_DISTANCE_PX * 2)

    expect(close).not.toHaveBeenCalled()
  })

  it('leaves a scroll of the open menu alone', () => {
    state.open = true
    const { close, sidebar } = page()

    pointer('pointerdown', sidebar, { clientX: 200, clientY: 300 })
    pointer('pointermove', sidebar, { clientX: 198, clientY: 300 + SCROLL_INTENT_PX + 1 })
    pointer('pointermove', sidebar, { clientX: 200 - SWIPE_DISTANCE_PX * 2, clientY: 330 })

    expect(close).not.toHaveBeenCalled()
  })

  it('closes an open menu on a page that blocks opening it', () => {
    state.open = true
    state.blocksOpening = true
    const { close, sidebar } = page()

    drag(sidebar, 200, 300, -SWIPE_DISTANCE_PX)

    expect(close).toHaveBeenCalledOnce()
  })

  it('starts from the edge no more once the menu is open, and a swipe there closes nothing', () => {
    state.open = true
    const { open, close, content } = page()

    drag(content, X0, 300, SWIPE_DISTANCE_PX * 2)

    expect(open).not.toHaveBeenCalled()
    expect(close).not.toHaveBeenCalled()
  })
})

describe('useEdgeSwipeOffered', () => {
  afterEach(() => vi.unstubAllGlobals())

  function screen(matching: string[]) {
    vi.stubGlobal('matchMedia', (query: string) => ({
      matches: matching.includes(query),
      addEventListener: () => {},
      removeEventListener: () => {},
    }))
    let offered = false
    mount(
      defineComponent({
        setup() {
          const result = useEdgeSwipeOffered()
          offered = result.value
          return () => null
        },
      }),
    )
    return offered
  }

  it('is offered on a phone-sized screen with a touchscreen', () => {
    expect(screen(['(width <= 767px)', '(any-pointer: coarse)'])).toBe(true)
  })

  // A narrow desktop window has no finger to swipe with.
  it('is not offered on a narrow screen without a touchscreen', () => {
    expect(screen(['(width <= 767px)'])).toBe(false)
  })

  // A tablet or a touch laptop has the sidebar beside the page, not a drawer.
  it('is not offered on a wide screen with a touchscreen', () => {
    expect(screen(['(any-pointer: coarse)'])).toBe(false)
  })

  it('is not offered where matchMedia is missing', () => {
    expect(screen([])).toBe(false)
  })
})
