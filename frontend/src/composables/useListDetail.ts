import { computed, shallowRef } from 'vue'
import { WIDE_MEDIA_QUERY } from '@/utils/viewport'
import { useMediaQuery } from './useMediaQuery'

/**
 * Which row of a list its detail shows, as the trip log and the charge history pick theirs. On a
 * desktop the detail stands beside the list and always shows a row: the first until another is
 * picked. On a narrower screen it opens in a dialog once a row is picked, and closing the dialog
 * picks none again.
 *
 * `find` looks a picked row up by its key, so a row that changed comes back as it is now, and one
 * that went away falls back to the first. `first` is the row a desktop starts on.
 */
export function useListDetail<T, K>(find: (key: K) => T | undefined, first: () => T | undefined) {
  const wide = useMediaQuery(WIDE_MEDIA_QUERY)
  const pickedKey = shallowRef<K | null>(null)

  const selected = computed((): T | null => {
    const picked = pickedKey.value === null ? undefined : find(pickedKey.value)
    if (picked !== undefined) return picked
    return wide.value ? (first() ?? null) : null
  })

  /** True while the detail is in a dialog rather than beside the list. */
  const inDialog = computed(() => !wide.value && selected.value !== null)

  function pick(key: K) {
    pickedKey.value = key
  }

  /** Back to no pick: the first row on a desktop, the dialog closed on a narrower screen. */
  function clear() {
    pickedKey.value = null
  }

  return { wide, selected, inDialog, pick, clear }
}
