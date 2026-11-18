import { onUnmounted, watch, type Ref } from 'vue'
import { scrollParent } from '@/utils/viewport'

/** The custom property the room is written to, for the element's stylesheet to cap it with. */
export const ROOM_BELOW_PROPERTY = '--room-below'

/**
 * Keeps `--room-below` on the element set to the height from its top to the bottom of the column
 * it scrolls in (or the window), in pixels, so its stylesheet can keep it from reaching past the
 * bottom of the screen: a sticky panel that starts lower down the page than the top it sticks to.
 *
 * Measured again as the page scrolls, as the window is resized, and as the content above it
 * grows or shrinks and moves it. Only a custom property is set; the stylesheet decides what to do
 * with it. Nothing is set while there is no element.
 */
export function useRoomBelow(element: Readonly<Ref<HTMLElement | null>>) {
  let scroller: HTMLElement | null = null
  let resizeObserver: ResizeObserver | null = null
  let frame: number | null = null

  function measure() {
    frame = null
    const el = element.value
    if (!el) return
    const bottom = scroller ? scroller.getBoundingClientRect().bottom : window.innerHeight
    const room = Math.max(0, Math.round(bottom - el.getBoundingClientRect().top))
    el.style.setProperty(ROOM_BELOW_PROPERTY, `${room}px`)
  }

  // At most once a frame, however many scroll events arrive in it.
  function schedule() {
    if (frame === null) frame = requestAnimationFrame(measure)
  }

  function stop() {
    scroller?.removeEventListener('scroll', schedule)
    window.removeEventListener('resize', schedule)
    resizeObserver?.disconnect()
    resizeObserver = null
    if (frame !== null) cancelAnimationFrame(frame)
    frame = null
    scroller = null
  }

  watch(
    element,
    (el) => {
      stop()
      if (!el) return
      scroller = scrollParent(el)
      scroller?.addEventListener('scroll', schedule, { passive: true })
      window.addEventListener('resize', schedule)
      // The page's content changing height above the element moves it without a scroll.
      if (typeof ResizeObserver !== 'undefined') {
        resizeObserver = new ResizeObserver(schedule)
        resizeObserver.observe(scroller?.firstElementChild ?? document.body)
      }
      measure()
    },
    { immediate: true, flush: 'post' },
  )

  onUnmounted(stop)
}
