import type { App } from 'vue'
import { apiUrl } from '@/services/apiCore'

/** What the API's /api/client-errors takes. */
export interface ClientErrorReport {
  message: string
  stack?: string
  page: string
  source?: string
}

// At most this many reports per page load, and each distinct message once: an error inside a
// render loop or a timer would otherwise post on every tick.
const MAX_REPORTS = 10
const reported = new Set<string>()

// Browser chatter that is not the app's fault and that nobody could act on.
const IGNORED: readonly RegExp[] = [/ResizeObserver loop/i]

/** For the specs: forget what this page load has reported. */
export function resetErrorReporting(): void {
  reported.clear()
}

/**
 * Sends an error the app ran into to the API, which writes it to its log. Fire and forget: a
 * report that cannot be delivered is dropped rather than reported in turn. Plain fetch rather than
 * apiCore, so a signed-out answer never sets off the sign-out handling.
 */
export function reportError(error: unknown, source?: string): void {
  const message = error instanceof Error ? `${error.name}: ${error.message}` : String(error)
  if (!message || IGNORED.some((pattern) => pattern.test(message))) return
  if (reported.has(message) || reported.size >= MAX_REPORTS) return
  reported.add(message)

  const report: ClientErrorReport = {
    message,
    stack: error instanceof Error ? error.stack : undefined,
    page: window.location.pathname,
    source,
  }
  void fetch(apiUrl('/api/client-errors'), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(report),
    credentials: 'include',
    keepalive: true,
  }).catch(() => {})
}

/**
 * Reports the errors Vue catches in components, and the ones nothing caught (a script error, a
 * rejected promise nobody awaited), while someone is signed in: the API only takes reports from
 * a session. They still reach the console as before.
 */
export function installErrorReporting(app: App, isSignedIn: () => boolean): void {
  app.config.errorHandler = (error, _instance, info) => {
    console.error(error)
    if (isSignedIn()) reportError(error, `Vue: ${info}`)
  }
  window.addEventListener('error', (event) => {
    if (!isSignedIn()) return
    const where = event.filename ? `${event.filename}:${event.lineno}:${event.colno}` : undefined
    reportError(event.error ?? event.message, where)
  })
  window.addEventListener('unhandledrejection', (event) => {
    if (isSignedIn()) reportError(event.reason, 'unhandled promise rejection')
  })
}
