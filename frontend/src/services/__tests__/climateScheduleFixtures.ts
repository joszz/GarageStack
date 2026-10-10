import type { ClimateSchedule } from '@/services/climateScheduleApi'

/**
 * A weekday 07:30 schedule as the API returns it, shared by the climate schedule specs. Not a
 * spec itself, so importing it does not run anything twice.
 */
export function schedule(overrides: Partial<ClimateSchedule> = {}): ClimateSchedule {
  return {
    id: 1,
    name: null,
    enabled: true,
    startTime: '07:30',
    days: [1, 2, 3, 4, 5],
    timeZoneId: 'Europe/Amsterdam',
    mode: 'on',
    temperatureC: 21,
    rearDefroster: false,
    seatLeftLevel: 0,
    seatRightLevel: 0,
    onlyBelowC: null,
    onlyAboveC: null,
    nextRunUtc: '2026-10-12T05:30:00Z',
    lastRunAt: null,
    lastRunOutcome: null,
    lastRunFailedCommand: null,
    lastRunDetail: null,
    ...overrides,
  }
}
