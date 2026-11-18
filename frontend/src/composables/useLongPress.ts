import { onUnmounted } from 'vue'

// How long a finger rests on a card before the press counts as long, as on a phone's home screen.
export const LONG_PRESS_MS = 500

// A finger never rests perfectly still: this much drift still counts as holding, more is a scroll.
export const LONG_PRESS_TOLERANCE_PX = 10

// A finger put down this soon after the page scrolled most likely caught a fling to stop it, and
// stays to read rather than to press.
export const SCROLL_SETTLE_MS = 150

// How long after the finger lifts its click may still come: WebKit sends it a little later.
export const LIFT_CLICK_MS = 400

/**
 * Marks a part of a card where holding a finger keeps its own meaning, such as reading a chart's
 * value or selecting an address to copy: a long press there does not open the layout editor.
 */
export const LONG_PRESS_EXEMPT_CLASS = 'long-press-exempt'

const LIFT_EVENTS = ['pointerup', 'pointercancel', 'touchend', 'touchcancel'] as const

/**
 * A long press with a finger on any card in a grid, the way a phone's home screen enters its
 * edit mode. Bind the result to the grid (`v-bind`): a press on one of its cards, not on the
 * gap between them, that rests for {@link LONG_PRESS_MS} calls `onLongPress`. A mouse or pen
 * never does: they have the edit button. Moving the finger, lifting it, the page scrolling or
 * the browser taking over to scroll cancels the press, and a press on a part marked with
 * {@link LONG_PRESS_EXEMPT_CLASS} never starts one. Once it fired, the click and context menu
 * that the same press may still bring are swallowed, so the card under the finger does not open
 * as well.
 */
export function useLongPress(onLongPress: () => void, ms = LONG_PRESS_MS) {
  let timer: ReturnType<typeof setTimeout> | undefined
  let start: { x: number; y: number; target: EventTarget } | null = null
  let lastScrollAt = Number.NEGATIVE_INFINITY

  // While swallowing: what the lift listeners were added to, and the timer that ends it.
  let liftTargets: EventTarget[] = []
  let liftTimer: ReturnType<typeof setTimeout> | undefined

  function cancel() {
    clearTimeout(timer)
    timer = undefined
    start = null
  }

  function onScroll(event: Event) {
    lastScrollAt = event.timeStamp
    cancel()
  }
  // Scroll events do not bubble, but a capturing listener on the window hears every scroller's.
  window.addEventListener('scroll', onScroll, { capture: true, passive: true })

  function swallow(event: Event) {
    event.preventDefault()
    event.stopPropagation()
  }

  function stopSwallowing() {
    clearTimeout(liftTimer)
    window.removeEventListener('click', swallow, true)
    window.removeEventListener('contextmenu', swallow, true)
    window.removeEventListener('pointerdown', stopSwallowing, true)
    for (const target of liftTargets)
      for (const type of LIFT_EVENTS) target.removeEventListener(type, lifted, true)
    liftTargets = []
  }

  function lifted() {
    clearTimeout(liftTimer)
    liftTimer = setTimeout(stopSwallowing, LIFT_CLICK_MS)
  }

  function swallowRestOfPress(pressed: EventTarget) {
    stopSwallowing()
    // Edit mode replaces the grid under the finger, so whatever the press still brings lands
    // outside it: catch that on the window.
    window.addEventListener('click', swallow, true)
    window.addEventListener('contextmenu', swallow, true)
    // A new touch means the finger that pressed is up, whether or not its lift was heard: its
    // own click is not this press's.
    window.addEventListener('pointerdown', stopSwallowing, true)
    // WebKit keeps sending a touch's events to the element it began on, even once removed from
    // the page, where they never reach the window: hear the lift on that element as well.
    liftTargets = [window, pressed]
    for (const target of liftTargets)
      for (const type of LIFT_EVENTS) target.addEventListener(type, lifted, true)
  }

  function fire() {
    if (!start) return
    const pressed = start.target
    cancel()
    swallowRestOfPress(pressed)
    // Chrome refuses a vibration before the first tap on the page and logs it as an intervention.
    if (navigator.userActivation?.hasBeenActive) navigator.vibrate?.(10)
    onLongPress()
  }

  function onPointerdown(event: PointerEvent) {
    cancel()
    if (event.pointerType !== 'touch' || !event.isPrimary) return
    const target = event.target
    // The grid itself is the gap between its cards.
    if (!(target instanceof Element) || target === event.currentTarget) return
    if (target.closest(`.${LONG_PRESS_EXEMPT_CLASS}`)) return
    if (event.timeStamp - lastScrollAt < SCROLL_SETTLE_MS) return
    start = { x: event.clientX, y: event.clientY, target }
    timer = setTimeout(fire, ms)
  }

  function onPointermove(event: PointerEvent) {
    if (!start) return
    const dx = event.clientX - start.x
    const dy = event.clientY - start.y
    if (Math.hypot(dx, dy) > LONG_PRESS_TOLERANCE_PX) cancel()
  }

  // Android opens its context menu (copy, open image) on a long press of its own, at about the
  // same moment and sometimes a little sooner: that is the long press too, and gets no menu.
  function onContextmenu(event: Event) {
    if (!start) return
    event.preventDefault()
    fire()
  }

  onUnmounted(() => {
    cancel()
    stopSwallowing()
    window.removeEventListener('scroll', onScroll, true)
  })

  return {
    onPointerdown,
    onPointermove,
    onPointerup: cancel,
    onPointercancel: cancel,
    onContextmenu,
  }
}
