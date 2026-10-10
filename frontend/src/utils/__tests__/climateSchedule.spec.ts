import { describe, it, expect, afterEach } from 'vitest'
import {
  commandLabel,
  daysSummary,
  needsAttention,
  runLabel,
  runParts,
  settingsSummary,
  weekdayName,
} from '../climateSchedule'
import { setRegionalFormat } from '@/utils/format'
import { METRIC_UNITS, UnitFormatter } from '@/utils/units'
import { schedule } from '@/services/__tests__/climateScheduleFixtures'

const t = (key: string, named?: Record<string, unknown>) =>
  named ? `${key}(${Object.values(named).join(',')})` : key
const celsius = new UnitFormatter(METRIC_UNITS, (k) => (k === 'units.celsius' ? '°C' : k))

// ICU puts a narrow no-break space before AM and PM.
const plain = (text: string) => text.replace(/\s/g, ' ')

afterEach(() => setRegionalFormat({ region: null, hourCycle: 'h23' }))

describe('daysSummary', () => {
  it('names the common choices', () => {
    expect(daysSummary([], t)).toBe('climateSchedules.days.once')
    expect(daysSummary([1, 2, 3, 4, 5, 6, 7], t)).toBe('climateSchedules.days.everyDay')
    expect(daysSummary([5, 4, 3, 2, 1], t)).toBe('climateSchedules.days.weekdays')
    expect(daysSummary([6, 7], t)).toBe('climateSchedules.days.weekend')
  })

  it('lists any other days in weekday order', () => {
    expect(daysSummary([5, 1, 3], t)).toBe('Mon, Wed, Fri')
  })
})

describe('weekdayName', () => {
  it('reads ISO weekdays from Monday', () => {
    expect(weekdayName(1)).toBe('Mon')
    expect(weekdayName(7, 'long')).toBe('Sunday')
  })
})

describe('runLabel', () => {
  const now = new Date(2026, 9, 9, 12, 0)

  it('says today, tomorrow or yesterday for runs close by', () => {
    expect(runLabel(new Date(2026, 9, 9, 18, 0).toISOString(), now, t)).toBe(
      'climateSchedules.when.today(18:00)',
    )
    expect(runLabel(new Date(2026, 9, 10, 7, 30).toISOString(), now, t)).toBe(
      'climateSchedules.when.tomorrow(07:30)',
    )
    expect(runLabel(new Date(2026, 9, 8, 7, 30).toISOString(), now, t)).toBe(
      'climateSchedules.when.yesterday(07:30)',
    )
  })

  it('names the weekday within the week, and the date beyond it', () => {
    expect(runLabel(new Date(2026, 9, 12, 7, 30).toISOString(), now, t)).toBe('Monday 07:30')
    expect(runLabel(new Date(2026, 9, 20, 7, 30).toISOString(), now, t)).toBe('Oct 20 07:30')
  })

  it('writes the date in the order of the region', () => {
    setRegionalFormat({ region: 'NL', hourCycle: 'h23' })
    expect(runLabel(new Date(2026, 9, 20, 7, 30).toISOString(), now, t)).toBe('20 Oct 07:30')
  })
})

describe('runParts', () => {
  const now = new Date(2026, 9, 9, 12, 0)

  it('leaves the day out today, and names it otherwise', () => {
    setRegionalFormat({ region: 'NL', hourCycle: 'h23' })
    expect(runParts(new Date(2026, 9, 9, 18, 0).toISOString(), now)).toEqual({
      time: '18:00',
      day: null,
    })
    expect(runParts(new Date(2026, 9, 10, 7, 30).toISOString(), now)).toEqual({
      time: '7:30',
      day: 'Sat',
    })
    expect(runParts(new Date(2026, 9, 20, 7, 30).toISOString(), now)).toEqual({
      time: '7:30',
      day: '20 Oct',
    })
  })

  it('shows the time on a 12-hour clock when that is the chosen one', () => {
    setRegionalFormat({ region: 'US', hourCycle: 'h12' })
    expect(plain(runParts(new Date(2026, 9, 9, 18, 0).toISOString(), now).time)).toBe('6:00 PM')
  })
})

describe('settingsSummary', () => {
  it('leads with the temperature for normal climate, then the extras', () => {
    expect(settingsSummary(schedule({ rearDefroster: true, seatRightLevel: 1 }), t, celsius)).toBe(
      '21 °C · control.rearDefroster · climateSchedules.seatHeating',
    )
  })

  it('names the mode instead for fan only and front defrost', () => {
    expect(settingsSummary(schedule({ mode: 'front' }), t, celsius)).toBe('control.mode.front')
  })
})

describe('needsAttention and commandLabel', () => {
  it('flags a run that failed, went unanswered or was missed', () => {
    expect(needsAttention(schedule({ lastRunOutcome: 'failed' }))).toBe(true)
    expect(needsAttention(schedule({ lastRunOutcome: 'missed' }))).toBe(true)
    expect(needsAttention(schedule({ lastRunOutcome: 'started' }))).toBe(false)
    expect(needsAttention(schedule({ lastRunOutcome: 'skippedTemperature' }))).toBe(false)
    expect(needsAttention(schedule())).toBe(false)
  })

  it('names a command as the climate popup does', () => {
    expect(commandLabel('seat-left', t)).toBe('vehicle.climateDetail.seatLeft')
    expect(commandLabel('unknown-command', t)).toBe('unknown-command')
  })
})
