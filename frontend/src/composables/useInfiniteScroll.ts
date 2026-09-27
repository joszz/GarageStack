import { ref, computed, onUnmounted } from 'vue'
import type { Ref } from 'vue'

export function useInfiniteScroll<T>(items: Ref<T[]>, pageSize: number) {
  const displayCount = ref(pageSize)
  const sentinelRef = ref<HTMLElement | null>(null)
  let observer: IntersectionObserver | null = null

  const displayItems = computed(() => items.value.slice(0, displayCount.value))

  function reset() {
    displayCount.value = pageSize
  }

  /**
   * Starts growing the list whenever the sentinel scrolls into view inside `scrollRoot`. The
   * sentinel is the element bound to `sentinelRef`, unless one is given, for a list rendered by
   * a child component that exposes its own.
   */
  function observe(
    scrollRoot: HTMLElement | null,
    sentinel: HTMLElement | null = sentinelRef.value,
  ) {
    observer?.disconnect()
    if (!sentinel) return
    observer = new IntersectionObserver(
      ([entry]) => {
        if (entry?.isIntersecting && displayCount.value < items.value.length) {
          displayCount.value += pageSize
        }
      },
      { root: scrollRoot },
    )
    observer.observe(sentinel)
  }

  function disconnect() {
    observer?.disconnect()
    observer = null
  }

  onUnmounted(disconnect)

  return { displayItems, sentinelRef, reset, observe, disconnect }
}
