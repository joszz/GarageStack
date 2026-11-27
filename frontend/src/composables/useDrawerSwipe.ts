import { computed, onMounted, onUnmounted, type ComputedRef } from 'vue'
import { PHONE_MEDIA_QUERY } from '@/utils/viewport'
import { useMediaQuery } from './useMediaQuery'

/** A finger on the screen: the phone and tablet pointer, not a laptop's trackpad or a mouse. */
const TOUCH_QUERY = '(any-pointer: coarse)'

/**
 * The nearest to the left edge a finger can land and still start the swipe. A phone's own back
 * gesture takes the very edge, and the page cannot stop it, so the swipe starts clear of it:
 * landing nearer than this is left to the system, and never both at once.
 */
export const EDGE_START_MIN_PX = 30

/**
 * The farthest from the left edge a finger can land and still start the swipe. The band from the
 * minimum to here is wide enough to hit with a thumb, without taking over the page.
 */
export const EDGE_START_MAX_PX = 84

/**
 * What a finger that lands here is already using for a sideways movement of its own, so the swipe
 * leaves it be: a map or a chart pans, and a row of chips scrolls (see {@link scrollsSideways}).
 */
const OWN_GESTURE_SELECTOR = 'canvas, .maplibregl-map, .leaflet-container'

/** Whether `element`, or something around it, scrolls sideways under a finger. */
function scrollsSideways(element: Element): boolean {
  for (
    let el: Element | null = element;
    el && el !== document.documentElement;
    el = el.parentElement
  ) {
    if (el.scrollWidth <= el.clientWidth + 1) continue
    const { overflowX } = getComputedStyle(el)
    if (overflowX === 'auto' || overflowX === 'scroll') return true
  }
  return false
}

/** How far the finger travels sideways for a swipe to count. */
export const SWIPE_DISTANCE_PX = 56

/** The sideways travel is at least this many times the vertical one, or it is a scroll. */
export const SWIPE_DIRECTION_RATIO = 1.5

/** Vertical travel past which the gesture is a scroll, whatever the finger does next. */
export const SCROLL_INTENT_PX = 12

/** Every dialog and panel marks itself like this; none is swiped over. */
const MODAL_SELECTOR = '[aria-modal="true"]'

/**
 * Whether swiping the menu open and shut is offered here: on a phone-sized screen with a
 * touchscreen. A narrow desktop window has no finger to swipe with, and a tablet has a sidebar.
 */
export function useEdgeSwipeOffered(): ComputedRef<boolean> {
  const narrow = useMediaQuery(PHONE_MEDIA_QUERY)
  const touch = useMediaQuery(TOUCH_QUERY)
  return computed(() => narrow.value && touch.value)
}

export interface DrawerSwipeOptions {
  /** Whether swiping is on at all: the setting, and a screen that offers it. */
  enabled: () => boolean
  /** Whether the page has a swipe of its own at the edge (a map pans on any touch). */
  blocksOpening: () => boolean
  isOpen: () => boolean
  open: () => void
  close: () => void
  /** What the open menu is made of, so a swipe that starts on it, or on its backdrop, closes it. */
  closeFrom: string
}

type Direction = 'open' | 'close'

/**
 * Opens the mobile menu with a swipe that starts a little in from the left edge of the screen
 * (EDGE_START_MIN_PX to EDGE_START_MAX_PX) and moves right, and closes it with a swipe to the left
 * that starts on the menu or its backdrop. Listens on the window, so nothing sits over the page to
 * catch taps meant for it, and mounted once, in App.vue.
 *
 * A swipe is a decision, not a drag: once the finger has gone far enough sideways, and not mostly
 * up or down, the menu slides as it does for its button. Scrolling is left alone: a finger that
 * moves up or down first is a scroll, and the browser taking the touch over (touchcancel) ends it.
 * So is whatever already moves sideways under the finger, a map, a chart or a row that scrolls.
 *
 * The phone's own back gesture takes the very edge and the page cannot stop it, so the swipe
 * starts clear of the edge rather than at it: the two never start from the same touch.
 */
export function useDrawerSwipe(options: DrawerSwipeOptions): void {
  let swipe: { id: number; x: number; y: number; direction: Direction } | null = null

  function onPointerdown(event: PointerEvent) {
    swipe = null
    if (event.pointerType !== 'touch' || !event.isPrimary || !options.enabled()) return

    if (!options.isOpen()) {
      if (event.clientX < EDGE_START_MIN_PX || event.clientX > EDGE_START_MAX_PX) return
      if (options.blocksOpening() || document.querySelector(MODAL_SELECTOR)) return
      const target = event.target
      if (
        target instanceof Element &&
        (target.closest(OWN_GESTURE_SELECTOR) || scrollsSideways(target))
      )
        return
      swipe = { id: event.pointerId, x: event.clientX, y: event.clientY, direction: 'open' }
    } else if (event.target instanceof Element && event.target.closest(options.closeFrom)) {
      swipe = { id: event.pointerId, x: event.clientX, y: event.clientY, direction: 'close' }
    }
  }

  /** Where the finger is now: the swipe's decision, whichever event reported it. */
  function follow(x: number, y: number) {
    if (!swipe) return
    const dx = x - swipe.x
    const dy = y - swipe.y

    if (Math.abs(dy) > SCROLL_INTENT_PX && Math.abs(dy) > Math.abs(dx)) {
      swipe = null
      return
    }

    const travelled = swipe.direction === 'open' ? dx : -dx
    if (travelled < SWIPE_DISTANCE_PX || travelled <= SWIPE_DIRECTION_RATIO * Math.abs(dy)) return

    const { direction } = swipe
    swipe = null
    if (direction === 'open') options.open()
    else options.close()
  }

  function onPointermove(event: PointerEvent) {
    if (swipe && event.pointerId === swipe.id) follow(event.clientX, event.clientY)
  }

  // Some browsers (Chrome on Android) take a sideways drag for a pan of their own after a few
  // pixels and cancel the pointer, which stops its pointermoves, but the touch events go on. So the
  // touch follows the same swipe, and a cancelled pointer does not end it: a finger that goes
  // vertical is a scroll by its own travel (see follow), and lifting it ends the gesture.
  function onTouchmove(event: TouchEvent) {
    const touch = event.touches[0]
    if (event.touches.length > 1) swipe = null
    else if (touch) follow(touch.clientX, touch.clientY)
  }

  function onEnd() {
    swipe = null
  }

  // Capturing, so a page that stops the event on its way down (a map) is still heard to start.
  const passive = { passive: true, capture: true } as const
  onMounted(() => {
    window.addEventListener('pointerdown', onPointerdown, passive)
    window.addEventListener('pointermove', onPointermove, passive)
    window.addEventListener('pointerup', onEnd, passive)
    window.addEventListener('touchmove', onTouchmove, passive)
    window.addEventListener('touchend', onEnd, passive)
    window.addEventListener('touchcancel', onEnd, passive)
  })
  onUnmounted(() => {
    window.removeEventListener('pointerdown', onPointerdown, passive)
    window.removeEventListener('pointermove', onPointermove, passive)
    window.removeEventListener('pointerup', onEnd, passive)
    window.removeEventListener('touchmove', onTouchmove, passive)
    window.removeEventListener('touchend', onEnd, passive)
    window.removeEventListener('touchcancel', onEnd, passive)
  })
}
