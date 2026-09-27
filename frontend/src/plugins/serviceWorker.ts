/**
 * Keeps the page on the service worker's current version: a new worker replacing the one in
 * control reloads the page once, and a worker that fails to install is unregistered so the next
 * load starts clean.
 */
export function watchServiceWorkerUpdates(): void {
  if (!('serviceWorker' in navigator)) return

  let refreshing = false
  let controller = navigator.serviceWorker.controller
  navigator.serviceWorker.addEventListener('controllerchange', () => {
    const previous = controller
    controller = navigator.serviceWorker.controller
    // A first visit has no controller until the fresh worker claims the page, and that worker
    // serves the very build the page already runs. Reloading for it loaded every first visit
    // twice, which Lighthouse (it clears service workers before each run) scored as a redirect.
    if (!previous || refreshing) return
    refreshing = true
    window.location.reload()
  })

  // If a SW install fails (e.g. stale precache manifest after deploy while
  // sw.js was HTTP-cached as immutable), unregister and reload so the next
  // load fetches a fresh sw.js and installs cleanly.
  const watchInstalling = (sw: ServiceWorker, reg: ServiceWorkerRegistration) =>
    sw.addEventListener('statechange', () => {
      if (sw.state === 'redundant') reg.unregister().then(() => window.location.reload())
    })

  window.addEventListener('load', () => {
    navigator.serviceWorker.getRegistration('/').then((reg) => {
      if (!reg) return
      if (reg.installing) watchInstalling(reg.installing, reg)
      reg.addEventListener('updatefound', () => {
        if (reg.installing) watchInstalling(reg.installing, reg)
      })
    })
  })
}
