import type { Router } from 'vue-router'

/**
 * What happens when the API answers 401: the session is gone, so the stored one is dropped and
 * the login page shown, with the page the user was on as the place to come back to.
 *
 * Waits for the first navigation to settle first. A signed-out visitor's own session check is
 * answered 401 too, while the router guard is still sending them to the login page with the
 * address they asked for (an app page, or the API reference). Replacing that route would drop the
 * address, so a login page already showing is left as it is.
 */
export function createUnauthorizedRedirect(
  router: Router,
  logout: () => Promise<void>,
  done: () => void,
): () => void {
  return () => {
    void logout()
      .catch(() => {})
      .then(() => router.isReady())
      .catch(() => {})
      .then(() => {
        const current = router.currentRoute.value
        if (current.name === 'login') return
        return router.replace({ name: 'login', query: { redirect: current.fullPath } })
      })
      .finally(done)
  }
}
