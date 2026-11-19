// What the service worker keeps of the API's answers, so the statistics, the map, the trip log and
// the other pages still show what this browser last loaded when the server cannot be reached (no
// signal, or away from home without the VPN). Shared with the page, which reads the stamp below
// to say an answer is a saved one, and forgets the cache on sign-out.

/** The service worker's cache of API answers. */
export const SAVED_API_CACHE = 'garagestack-api'
/**
 * When a saved answer arrived from the server (ISO 8601). The worker stamps it on the copy it
 * keeps, so an answer that carries it came from that copy and not from the server.
 */
export const SAVED_AT_HEADER = 'X-GarageStack-Saved-At'
/** Each page keeps a handful of answers; this leaves room for many periods and months. */
export const SAVED_API_MAX_ENTRIES = 300
/** An answer nobody asked for in three months is dropped: the page would ask for another by then. */
export const SAVED_API_MAX_AGE_SECONDS = 90 * 24 * 60 * 60

// The answers a page reads to show the car and its history, the trip log's routes included (read
// from the trip editor's endpoint). Not the log viewer (not available offline), exports, sign-in,
// settings (the stores keep their own copy) or map layers fetched per map view.
const SAVED_PATHS: readonly RegExp[] = [
  /^\/api\/vehicles$/,
  /^\/api\/vehicles\/tyre-pressure-thresholds$/,
  /^\/api\/vehicles\/[^/]+\/(config|status|tyres|stats|stats\/daily|rdw)$/,
  /^\/api\/vehicles\/[^/]+\/(charge-sessions|charge-sessions\/power)$/,
  /^\/api\/vehicles\/[^/]+\/(trips|trips\/latest|trips\/log|trips\/\d+\/editor)$/,
  /^\/api\/vehicles\/[^/]+\/(maintenance|maintenance\/\d+\/log|climate-schedules|commute-places)$/,
  /^\/api\/notifications$/,
]

/** Whether the worker keeps the answer to a GET for this path. */
export function isSavedApiPath(pathname: string): boolean {
  return SAVED_PATHS.some((pattern) => pattern.test(pathname))
}

const MS_PER_DAY = 24 * 60 * 60 * 1000
/** A window whose end is this close to when it was asked for runs up to "now". */
const ENDS_NOW_SLACK_MS = 2 * MS_PER_DAY
/** Two windows ending now are the same period when their lengths differ by no more than this. */
const SPAN_TOLERANCE = 0.1

/**
 * The period a request asks for, with `from` and `to` set apart from the rest of its address.
 * Null for a request without a `from`, which only its exact address answers.
 */
interface Period {
  /** The path and every other parameter, in a stable order. */
  base: string
  from: number
  /** Whether the period runs up to the moment it was asked for (no `to`, or `to` is today). */
  endsNow: boolean
  span: number
}

function periodOf(url: URL, at: number): Period | null {
  const params = new URLSearchParams(url.search)
  const fromText = params.get('from')
  if (fromText === null) return null
  const from = Date.parse(fromText)
  const toText = params.get('to')
  const to = toText === null ? at : Date.parse(toText)
  if (Number.isNaN(from) || Number.isNaN(to)) return null
  params.delete('from')
  params.delete('to')
  params.sort()
  return {
    base: `${url.pathname}?${params.toString()}`,
    from,
    endsNow: Math.abs(to - at) <= ENDS_NOW_SLACK_MS,
    span: to - from,
  }
}

/**
 * Whether two periods are the same one asked for at different moments: "the last 30 days" from
 * yesterday and from today, or "this year" with one more day in it. A fixed period (a month in the
 * trip log, a custom range) is only ever answered by its own address.
 */
function samePeriod(a: Period, b: Period): boolean {
  if (a.base !== b.base || !a.endsNow || !b.endsNow) return false
  if (a.from === b.from) return true
  const longest = Math.max(a.span, b.span)
  return longest > 0 && Math.abs(a.span - b.span) / longest <= SPAN_TOLERANCE
}

/** A saved answer's address and when it arrived. */
export interface SavedEntry {
  url: string
  savedAt: number
}

/**
 * The saved answer that best stands in for a request no saved answer matches exactly: the same
 * period, asked for on an earlier day. The closest in length wins, then the newest. Null when
 * nothing saved is the same period.
 */
export function pickSavedStandIn(
  requestUrl: string,
  now: number,
  saved: readonly SavedEntry[],
): string | null {
  const wanted = periodOf(new URL(requestUrl), now)
  if (wanted === null) return null
  let best: { url: string; gap: number; savedAt: number } | null = null
  for (const entry of saved) {
    const period = periodOf(new URL(entry.url), entry.savedAt)
    if (period === null || !samePeriod(wanted, period)) continue
    const gap = Math.abs(period.span - wanted.span)
    if (best === null || gap < best.gap || (gap === best.gap && entry.savedAt > best.savedAt)) {
      best = { url: entry.url, gap, savedAt: entry.savedAt }
    }
  }
  return best?.url ?? null
}

/**
 * The saved answers a newly saved one replaces: the same period asked for earlier. Without this
 * every visit to a moving period ("the last 7 days", down to the millisecond) would keep a copy.
 */
export function supersededBy(
  savedUrl: string,
  savedAt: number,
  others: readonly SavedEntry[],
): string[] {
  const period = periodOf(new URL(savedUrl), savedAt)
  if (period === null) return []
  return others
    .filter((entry) => {
      if (entry.url === savedUrl) return false
      const other = periodOf(new URL(entry.url), entry.savedAt)
      return other !== null && samePeriod(period, other)
    })
    .map((entry) => entry.url)
}

/** When the answer arrived from the server if it is a saved one, or null for a live answer. */
export function savedAtOf(response: Response): string | null {
  return response.headers?.get(SAVED_AT_HEADER) ?? null
}
