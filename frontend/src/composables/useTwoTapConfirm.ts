import { onUnmounted, ref, type Ref } from 'vue'

// How long a first tap waits for the second before it lapses.
export const CONFIRM_WINDOW_MS = 4000

/**
 * A two-tap confirmation for something that cannot be undone: the first tap on an item arms it
 * (its button then asks), a second tap on the same item within a few seconds goes ahead. Waiting,
 * or arming another item, disarms. One instance covers a whole list, keyed by item.
 */
export function useTwoTapConfirm<K>(windowMs = CONFIRM_WINDOW_MS) {
  const armed = ref(null) as Ref<K | null>
  let timer: ReturnType<typeof setTimeout> | undefined

  function disarm() {
    clearTimeout(timer)
    armed.value = null
  }

  /** True when this tap confirms `key`; false when it only armed it. */
  function tap(key: K): boolean {
    if (armed.value === key) {
      disarm()
      return true
    }
    clearTimeout(timer)
    armed.value = key
    timer = setTimeout(disarm, windowMs)
    return false
  }

  onUnmounted(() => clearTimeout(timer))

  return { armed, tap, disarm }
}
