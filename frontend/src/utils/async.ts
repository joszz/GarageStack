/** Resolves after `ms` milliseconds. */
export function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

/** How long whenPageIdle waits for an idle moment at most, once the page has loaded. */
export const PAGE_IDLE_TIMEOUT_MS = 2000

/**
 * Resolves once the page has finished loading and the browser has a moment to spare, or after
 * PAGE_IDLE_TIMEOUT_MS of waiting for one. For work the page does not need to show itself, so
 * that it does not compete with what the page does need. A browser without requestIdleCallback
 * goes ahead right after the load.
 */
export function whenPageIdle(): Promise<void> {
  const loaded =
    document.readyState === 'complete'
      ? Promise.resolve()
      : new Promise<void>((resolve) =>
          window.addEventListener('load', () => resolve(), { once: true }),
        )
  return loaded.then(
    () =>
      new Promise<void>((resolve) => {
        if (typeof window.requestIdleCallback === 'function') {
          window.requestIdleCallback(() => resolve(), { timeout: PAGE_IDLE_TIMEOUT_MS })
        } else {
          setTimeout(resolve, 0)
        }
      }),
  )
}
