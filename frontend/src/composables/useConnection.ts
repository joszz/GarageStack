import { computed, reactive, readonly, ref } from 'vue'
import { apiUrl, setConnectionListener } from '@/services/apiCore'

/** How often to try the server again while it cannot be reached but the browser has a network. */
export const SERVER_RETRY_MS = 30_000
// Answers who is signed in, or that nobody is: either way the server is there. Not one the
// service worker keeps, so the answer is always the server's own.
const RETRY_PATH = '/api/auth/me'
const SERVER_DOWN = new Set([502, 503, 504])

// One state for the whole app, like the notifications: every page and button reads the same.
// The browser's own word: false is certain, true only means a request may get through.
const browserOnline = ref(navigator.onLine !== false)
// A request found no server: no signal in a car park, or away from home without the VPN.
const serverUnreachable = ref(false)
// The oldest saved answer the open page shows, for the notice to say how old it may be.
const savedAt = ref<string | null>(null)

const state = readonly(
  reactive({
    reachable: computed(() => browserOnline.value && !serverUnreachable.value),
    savedAt,
  }),
)

let retryTimer: ReturnType<typeof setInterval> | null = null

function stopRetrying() {
  if (retryTimer !== null) clearInterval(retryTimer)
  retryTimer = null
}

async function retry() {
  try {
    const res = await fetch(apiUrl(RETRY_PATH), { credentials: 'include', cache: 'no-store' })
    if (!SERVER_DOWN.has(res.status)) answered()
  } catch {
    // Still nothing: tried again on the next tick.
  }
}

// Nothing on a page asks the server for anything while it is away (the buttons are hidden), so
// this asks on its own, and the pages hear it is back through `reachable`.
function startRetrying() {
  retryTimer ??= setInterval(() => {
    if (browserOnline.value) void retry()
  }, SERVER_RETRY_MS)
}

function answered() {
  serverUnreachable.value = false
  stopRetrying()
}

function answeredFromSave(at: string) {
  serverUnreachable.value = true
  if (savedAt.value === null || at < savedAt.value) savedAt.value = at
  startRetrying()
}

function unreachable() {
  serverUnreachable.value = true
  startRetrying()
}

function goOnline() {
  browserOnline.value = true
  // The browser is back on a network, but the server may still be out of reach: ask now.
  if (serverUnreachable.value) void retry()
}

function goOffline() {
  browserOnline.value = false
}

/**
 * Starts following the connection: the browser's online and offline events, and how every API
 * request goes. Called once at startup, before the first request (the session check).
 */
export function watchConnection(): void {
  window.addEventListener('online', goOnline)
  window.addEventListener('offline', goOffline)
  setConnectionListener({ answered, answeredFromSave, unreachable })
}

/** A new page opened: what the last one showed says nothing about how old this one's answers are. */
export function connectionPageChanged(): void {
  savedAt.value = null
}

/** Back to a fresh start, for tests. */
export function resetConnection(): void {
  window.removeEventListener('online', goOnline)
  window.removeEventListener('offline', goOffline)
  setConnectionListener(null)
  stopRetrying()
  browserOnline.value = navigator.onLine !== false
  serverUnreachable.value = false
  savedAt.value = null
}

/**
 * Whether GarageStack's server can be reached (`reachable`), and since when the open page shows
 * saved answers (`savedAt`). While it cannot be reached, the pages show what the service worker
 * saved and hide what needs the server: the commands, editing, the log viewer.
 */
export function useConnection() {
  return state
}
