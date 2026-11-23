import { buildQuery, request } from '@/services/apiCore'

/** Serilog's levels, as the API names them, lowest first. */
export const LOG_LEVELS = ['Verbose', 'Debug', 'Information', 'Warning', 'Error', 'Fatal'] as const
export type LogLevel = (typeof LOG_LEVELS)[number]

/** The levels worth starting from: Verbose is never written, and Fatal alone is next to nothing. */
export const LOG_LEVEL_CHOICES: readonly LogLevel[] = LOG_LEVELS.filter(
  (l) => l !== 'Verbose' && l !== 'Fatal',
)
export const DEFAULT_LOG_LEVEL: LogLevel = 'Information'

export type LogSourceName = 'api' | 'worker'
/** The files the log viewer reads from: both, or one of them. */
export const LOG_SOURCE_CHOICES = ['all', 'api', 'worker'] as const
export type LogSourceChoice = (typeof LOG_SOURCE_CHOICES)[number]

export interface LogEntry {
  /** Where the entry starts in its file: unique, and the same on every read. */
  id: string
  time: string
  level: LogLevel
  source: LogSourceName
  message: string
  /** An exception's lines, when the entry carried one. */
  exception: string | null
}

export interface LogPage {
  /** Newest first. */
  entries: LogEntry[]
  /** Each service, and whether the API can read its log files. */
  sources: { name: LogSourceName; available: boolean }[]
  /**
   * Goes back as `before` for the entries older than these. Null once nothing older is left, and
   * on a live read that does not reset.
   */
  older: string | null
  /** Goes back as `after` for what is written since. Null on an older page. */
  newer: string | null
  /** Whether the entries replace the list: a first page, or a live read that found more than a page. */
  reset: boolean
}

export interface LogQuery {
  source: LogSourceName | 'all'
  /** The lowest level shown. */
  level: LogLevel
  search?: string
  limit?: number
  /** An earlier page's `older`, for the page after it. */
  before?: string
  /** An earlier page's `newer`, for what was written since. */
  after?: string
}

export const logsApi = {
  page: ({ source, level, search, limit, before, after }: LogQuery) =>
    request<LogPage>(
      `/api/logs${buildQuery({ source, level, search: search || undefined, limit, before, after })}`,
    ),
}
