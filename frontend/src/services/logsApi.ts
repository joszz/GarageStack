import { buildQuery, request } from '@/services/apiCore'

/** Serilog's levels, as the API names them, lowest first. */
export const LOG_LEVELS = ['Verbose', 'Debug', 'Information', 'Warning', 'Error', 'Fatal'] as const
export type LogLevel = (typeof LOG_LEVELS)[number]

export type LogSourceName = 'api' | 'worker'

export interface LogEntry {
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
}

export interface LogQuery {
  source: LogSourceName | 'all'
  /** The lowest level shown. */
  level: LogLevel
  search?: string
  limit?: number
}

export const logsApi = {
  recent: ({ source, level, search, limit }: LogQuery) =>
    request<LogPage>(
      `/api/logs${buildQuery({ source, level, search: search || undefined, limit })}`,
    ),
}
