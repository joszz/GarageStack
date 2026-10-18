import { onUnmounted, ref } from 'vue'

/**
 * Calls `onReached` whenever the sentinel, an empty element after a list, scrolls into view or
 * comes within `margin` of it, so the list can grow before its end shows.
 */
export function useScrollSentinel(onReached: () => void, margin = '0px') {
  const sentinelRef = ref<HTMLElement | null>(null)
  let observer: IntersectionObserver | null = null
  let watched: HTMLElement | null = null

  /**
   * Starts watching inside `scrollRoot`. The sentinel is the element bound to `sentinelRef`,
   * unless one is given, for a list rendered by a child component that exposes its own.
   */
  function observe(
    scrollRoot: HTMLElement | null,
    sentinel: HTMLElement | null = sentinelRef.value,
  ) {
    disconnect()
    if (!sentinel) return
    watched = sentinel
    observer = new IntersectionObserver(
      ([entry]) => {
        if (entry?.isIntersecting) onReached()
      },
      { root: scrollRoot, rootMargin: margin },
    )
    observer.observe(sentinel)
  }

  /**
   * Asks again whether the sentinel is in view: the observer only reports a change, and a list
   * that grew without pushing the sentinel out of view has not changed anything.
   */
  function recheck() {
    if (!observer || !watched) return
    observer.unobserve(watched)
    observer.observe(watched)
  }

  function disconnect() {
    observer?.disconnect()
    observer = null
    watched = null
  }

  onUnmounted(disconnect)

  return { sentinelRef, observe, recheck, disconnect }
}
