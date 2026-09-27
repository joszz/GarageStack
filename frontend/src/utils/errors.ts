import { ApiError } from '@/services/apiCore'

/**
 * The i18n key of the message to show for a failure. The API names its reason with a code, which
 * is used when a translation exists for it; otherwise the HTTP status says enough to explain it.
 * Nothing technical (paths, status codes, English server text) ever reaches the page.
 *
 * @param hasMessage Whether a key has a translation (vue-i18n's `te`).
 */
export function errorMessageKey(error: unknown, hasMessage: (key: string) => boolean): string {
  if (error instanceof ApiError) {
    const coded = error.code ? `errors.codes.${error.code}` : null
    if (coded && hasMessage(coded)) return coded
    return statusMessageKey(error.status)
  }
  // fetch rejects with a TypeError when no answer arrived at all.
  if (error instanceof TypeError) return 'errors.offline'
  return 'errors.unknown'
}

function statusMessageKey(status: number): string {
  if (status === 400 || status === 422) return 'errors.badRequest'
  if (status === 401) return 'errors.unauthorized'
  if (status === 403) return 'errors.forbidden'
  if (status === 404) return 'errors.notFound'
  if (status === 409) return 'errors.conflict'
  if (status === 429) return 'errors.rateLimited'
  if (status >= 500) return 'errors.server'
  return 'errors.unknown'
}

/** Keeps whatever was thrown as an Error, so a store can hold one type of failure. */
export function asError(thrown: unknown): Error {
  return thrown instanceof Error ? thrown : new Error(String(thrown))
}
