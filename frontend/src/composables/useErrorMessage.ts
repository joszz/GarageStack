import { useI18n } from 'vue-i18n'
import { errorMessageKey } from '@/utils/errors'

/**
 * Turns a failure a store kept into the translated sentence to show for it, or null when there
 * is none. Translated at render time, so a language switch changes the message too.
 */
export function useErrorMessage() {
  const { t, te } = useI18n()
  return (error: unknown): string | null =>
    error == null ? null : t(errorMessageKey(error, (key) => te(key)))
}
