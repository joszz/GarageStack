import { apiUrl, request, send } from '@/services/apiCore'

/** The account's calendar link, as the API's CalendarLinkDto: never the link itself. */
export interface CalendarLink {
  exists: boolean
  createdAt: string | null
  /** When a calendar app last fetched the feed, to the minute. */
  lastUsedAt: string | null
}

/** A link just made: `token` is shown this once and never again. */
export interface CreatedCalendarLink {
  details: CalendarLink
  token: string
}

export const calendarApi = {
  status: () => request<CalendarLink>('/api/calendar/link'),
  /** Makes the account's link, replacing the one it had. */
  create: () => request<CreatedCalendarLink>('/api/calendar/link', { method: 'POST' }),
  stop: () => send('/api/calendar/link', 'DELETE'),
}

/**
 * The feed's address for a calendar app to subscribe to: on this page's own address, with the
 * token, and in `language` (the API reads `culture` for the events' texts).
 */
export function calendarFeedUrl(token: string, language: string): string {
  const url = new URL(apiUrl('/api/calendar/feed.ics'), window.location.origin)
  url.search = new URLSearchParams({ token, culture: language }).toString()
  return url.href
}

/** The same address as webcal:, which a calendar app opens as a subscription. */
export function webcalUrl(feedUrl: string): string {
  return feedUrl.replace(/^https?:/, 'webcal:')
}
