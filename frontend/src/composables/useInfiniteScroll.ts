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

  function observe(scrollRoot: HTMLElement | null) {
    observer?.disconnect()
    if (!sentinelRef.value) return
    observer = new IntersectionObserver(
      ([entry]) => {
        if (entry?.isIntersecting && displayCount.value < items.value.length) {
          displayCount.value += pageSize
        }
      },
      { root: scrollRoot },
    )
    observer.observe(sentinelRef.value)
  }

  function disconnect() {
    observer?.disconnect()
    observer = null
  }

  onUnmounted(disconnect)

  return { displayItems, sentinelRef, reset, observe, disconnect }
}
