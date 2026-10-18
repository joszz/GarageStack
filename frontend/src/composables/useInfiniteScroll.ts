import { ref, computed } from 'vue'
import type { Ref } from 'vue'
import { useScrollSentinel } from './useScrollSentinel'

/** Shows `items` a page at a time, one more each time the sentinel after them scrolls into view. */
export function useInfiniteScroll<T>(items: Ref<T[]>, pageSize: number) {
  const displayCount = ref(pageSize)

  const displayItems = computed(() => items.value.slice(0, displayCount.value))

  const { sentinelRef, observe, disconnect } = useScrollSentinel(() => {
    if (displayCount.value < items.value.length) displayCount.value += pageSize
  })

  function reset() {
    displayCount.value = pageSize
  }

  return { displayItems, sentinelRef, reset, observe, disconnect }
}
