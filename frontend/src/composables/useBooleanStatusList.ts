import { computed, type ComputedRef } from 'vue'

/** A candidate item before filtering - `open` is null when the vehicle hasn't reported this field. */
export type BooleanStatusCandidate = { key: string; label: string; open: boolean | null }

/**
 * Builds a reactive list of {key, label, open, ...} items from candidates whose `open` may be
 * null, dropping the null ones and narrowing `open` to `boolean` - the shared shape behind
 * DoorsCard/WindowsCard/LightsCard's per-item status lists (doors, windows, lights).
 */
export function useBooleanStatusList<T extends BooleanStatusCandidate>(
  candidates: () => T[],
): ComputedRef<(T & { open: boolean })[]> {
  return computed(() =>
    candidates().filter((item): item is T & { open: boolean } => item.open !== null),
  )
}
