import { computed, ref } from 'vue'
import type { Ref } from 'vue'
import { asError } from '@/utils/errors'

/**
 * Counts in-flight async actions so `loading` stays true until the last one settles, and runs
 * each action with its own error ref. Concurrent calls (e.g. Promise.all on mount) therefore
 * cannot have one action's success silently overwrite another action's error. Shared by the
 * vehicle and maintenance stores.
 */
export function useLoadingTracker() {
  const loadingCount = ref(0)
  const loading = computed(() => loadingCount.value > 0)

  async function withLoading(errorRef: Ref<Error | null>, fn: () => Promise<void>) {
    loadingCount.value++
    errorRef.value = null
    try {
      await fn()
    } catch (e) {
      errorRef.value = asError(e)
    } finally {
      loadingCount.value--
    }
  }

  return { loadingCount, loading, withLoading }
}
