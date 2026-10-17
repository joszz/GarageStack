import { ref } from 'vue'
import { useErrorMessage } from './useErrorMessage'

/**
 * A request a component makes itself rather than through a store: `loading` while it runs, the
 * translated reason in `error` once it failed. Only the newest call counts, so when a filter
 * changes twice, the first answer arriving last cannot replace the second's. An error stays until
 * a call goes through, so a page retrying on a timer does not blink its message on every try.
 */
export function useAsyncRequest() {
  const errorMessage = useErrorMessage()
  const loading = ref(false)
  const error = ref<string | null>(null)
  let latest = 0

  /**
   * Runs `request` and hands its result to `apply`, unless a newer call started meanwhile.
   * Resolves to whether this call's answer was the one taken.
   */
  async function run<T>(request: () => Promise<T>, apply: (result: T) => void): Promise<boolean> {
    const call = ++latest
    loading.value = true
    try {
      const result = await request()
      if (call !== latest) return false
      apply(result)
      error.value = null
      return true
    } catch (e) {
      if (call === latest) error.value = errorMessage(e)
      return false
    } finally {
      if (call === latest) loading.value = false
    }
  }

  return { loading, error, run }
}
