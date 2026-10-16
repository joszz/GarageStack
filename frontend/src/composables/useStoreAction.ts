import { ref } from 'vue'
import { useErrorMessage } from '@/composables/useErrorMessage'

/**
 * Runs a store action that reports failure through the store's error ref instead of throwing,
 * the way the maintenance and climate schedule stores do (`withLoading`). `busy` is true while
 * it runs, and a failure becomes the translated message in `error`. `run` resolves to whether
 * the action went through, so a dialog closes only when it did and otherwise stays open with
 * the reason.
 */
export function useStoreAction(failure: () => Error | null) {
  const errorMessage = useErrorMessage()
  const busy = ref(false)
  const error = ref<string | null>(null)

  async function run(action: () => Promise<unknown>): Promise<boolean> {
    error.value = null
    busy.value = true
    try {
      await action()
    } finally {
      busy.value = false
    }
    const failed = failure()
    if (failed) error.value = errorMessage(failed)
    return failed === null
  }

  return { busy, error, run }
}
