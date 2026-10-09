import { request, requestJson, send } from '@/services/apiCore'
import type { ClimateOnMode } from '@/utils/climate'

/** How a schedule's last run went. */
export type ClimateScheduleOutcome =
  | 'running'
  | 'started'
  | 'failed'
  | 'unconfirmed'
  | 'skippedDriving'
  | 'skippedTemperature'
  | 'missed'

/** ISO weekday: Monday 1 to Sunday 7. */
export type IsoWeekday = 1 | 2 | 3 | 4 | 5 | 6 | 7

/** What a schedule does and when, as the browser sends it. */
export interface ClimateScheduleRequest {
  name: string | null
  enabled: boolean
  /** The time of day as HH:mm, in timeZoneId. */
  startTime: string
  /** The weekdays it repeats on; none runs it once. */
  days: IsoWeekday[]
  /** An IANA time zone, such as Europe/Amsterdam. */
  timeZoneId: string
  mode: ClimateOnMode
  /** Whole degrees Celsius, used by normal climate only. */
  temperatureC: number
  rearDefroster: boolean
  seatLeftLevel: number
  seatRightLevel: number
  /** Runs only when it is colder than this outside (°C), or warmer than onlyAboveC. */
  onlyBelowC: number | null
  onlyAboveC: number | null
}

export interface ClimateSchedule extends ClimateScheduleRequest {
  id: number
  /** When it runs next (UTC), or null while it is switched off. */
  nextRunUtc: string | null
  lastRunAt: string | null
  lastRunOutcome: ClimateScheduleOutcome | null
  /** The command (e.g. seat-left) that failed or went unanswered on the last run. */
  lastRunFailedCommand: string | null
  /** The car's reason for that failure, as it gave it. */
  lastRunDetail: string | null
}

const base = (vin: string) => `/api/vehicles/${vin}/climate-schedules`

export const climateScheduleApi = {
  list: (vin: string) => request<ClimateSchedule[]>(base(vin)),
  create: (vin: string, body: ClimateScheduleRequest) =>
    requestJson<ClimateSchedule>(base(vin), 'POST', body),
  update: (vin: string, id: number, body: ClimateScheduleRequest) =>
    requestJson<ClimateSchedule>(`${base(vin)}/${id}`, 'PUT', body),
  delete: (vin: string, id: number) => send(`${base(vin)}/${id}`, 'DELETE'),
}
