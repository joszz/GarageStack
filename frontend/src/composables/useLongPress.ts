import { onUnmounted } from 'vue'

// How long a finger rests on a card before the press counts as long, as on a phone's home screen.
export const LONG_PRESS_MS = 500

// A finger never rests perfectly still: this much drift still counts as holding, more is a scroll.
export const LONG_PRESS_TOLERANCE_PX = 10

/**
 * A long press with a finger on any card in a grid, the way a phone's home screen enters its
 * edit mode. Bind the result to the grid (`v-bind`): a press on one of its cards, not on the
 * gap between them, that rests for {@link LONG_PRESS_MS} calls `onLongPress`. A mouse or pen
 * never does: they have the edit button. Moving the finger, lifting it or the browser taking
 * over to scroll cancels the press. Once it fired, the click and context menu that the same
 * press may still bring are swallowed, so the card under the finger does not open as well.
 */
export function useLongPress(onLongPress: () => void, ms = LONG_PRESS_MS) {
  let timer: ReturnType<typeof setTimeout> | undefined
  let start: { x: number; y: number } | null = null

  function cancel() {
    clearTimeout(timer)
    timer = undefined
    start = null
  }

  function swallow(event: Event) {
    event.preventDefault()
    event.stopPropagation()
  }

  function stopSwallowing() {
    window.removeEventListener('click', swallow, true)
    window.removeEventListener('contextmenu', swallow, true)
    window.removeEventListener('pointerup', stopSwallowingSoon, true)
    window.removeEventListener('pointercancel', stopSwallowingSoon, true)
  }

  // The click of the lifting finger comes in the same task as its pointerup, so wait one more.
  function stopSwallowingSoon() {
    setTimeout(stopSwallowing, 0)
  }

  function fire() {
    cancel()
    // Edit mode replaces the grid under the finger, so whatever the press still brings lands
    // outside it: catch that on the window.
    window.addEventListener('click', swallow, true)
    window.addEventListener('contextmenu', swallow, true)
    window.addEventListener('pointerup', stopSwallowingSoon, true)
    window.addEventListener('pointercancel', stopSwallowingSoon, true)
    navigator.vibrate?.(10)
    onLongPress()
  }

  function onPointerdown(event: PointerEvent) {
    cancel()
    if (event.pointerType !== 'touch' || !event.isPrimary) return
    // The grid itself is the gap between its cards.
    if (event.target === event.currentTarget) return
    start = { x: event.clientX, y: event.clientY }
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
  })

  return {
    onPointerdown,
    onPointermove,
    onPointerup: cancel,
    onPointercancel: cancel,
    onContextmenu,
  }
}
