const BASE_URL = import.meta.env.VITE_API_URL ?? ''

let unauthorizedHandler: (() => void) | null = null
let handlingUnauthorized = false

export function setUnauthorizedHandler(handler: (() => void) | null) {
  unauthorizedHandler = handler
}

export function clearUnauthorizedState() {
  handlingUnauthorized = false
}

// Absolute URL for an API path. Needed for links and WebSocket connections, which the browser
// resolves itself instead of going through request()/send().
export function apiUrl(path: string): string {
  return `${BASE_URL}${path}`
}

export class ApiError extends Error {
  status: number
  path: string

  constructor(status: number, path: string) {
    super(`API error ${status}: ${path}`)
    this.status = status
    this.path = path
  }
}

function handleResponse(res: Response, path: string) {
  if (res.status === 401 && !handlingUnauthorized) {
    handlingUnauthorized = true
    unauthorizedHandler?.()
  }
  if (!res.ok) throw new ApiError(res.status, path)
}

// The one place that knows how a JSON body travels: every POST/PUT/PATCH goes through here.
function jsonInit(method: string, body?: unknown): RequestInit {
  return {
    method,
    headers: { 'Content-Type': 'application/json' },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  }
}

/** Fetches `path` and parses the JSON response (undefined on 204). */
export async function request<T>(path: string, options?: RequestInit): Promise<T> {
  const res = await fetch(`${BASE_URL}${path}`, {
    ...options,
    credentials: 'include',
  })

  handleResponse(res, path)
  if (res.status === 204) return undefined as T
  return res.json() as Promise<T>
}

/** Sends a JSON body and parses the JSON response. */
export function requestJson<T>(path: string, method: string, body?: unknown): Promise<T> {
  return request<T>(path, jsonInit(method, body))
}

/** Sends a JSON body (or none) and ignores the response body, for endpoints that answer with an empty 200. */
export async function send(path: string, method: string, body?: unknown): Promise<void> {
  const res = await fetch(`${BASE_URL}${path}`, {
    ...jsonInit(method, body),
    credentials: 'include',
  })

  handleResponse(res, path)
}

// Builds a query string from scalar params, skipping undefined values. Returns an empty
// string when there's nothing to add, otherwise a string starting with '?'.
export function buildQuery(params: Record<string, string | number | boolean | undefined>): string {
  const search = new URLSearchParams()
  for (const [key, value] of Object.entries(params)) {
    if (value === undefined) continue
    search.set(key, String(value))
  }
  const qs = search.toString()
  return qs ? `?${qs}` : ''
}
