/**
 * Keeps the page on the service worker's current version: a new worker taking control reloads
 * the page once, and a worker that fails to install is unregistered so the next load starts
 * clean.
 */
export function watchServiceWorkerUpdates(): void {
  if (!('serviceWorker' in navigator)) return

  let refreshing = false
  navigator.serviceWorker.addEventListener('controllerchange', () => {
    if (refreshing) return
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
