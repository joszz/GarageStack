import { describe, it, expect, afterEach } from 'vitest'
import { i18n } from '@/i18n'
import {
  activeHourCycle,
  clockExample,
  dayPeriodNames,
  formatDate,
  formatDateTime,
  formatHour,
  formatNumber,
  formatTime,
  formatTimeOfDay,
  numberLocale,
  setRegionalFormat,
} from '@/utils/format'

// ICU puts a narrow no-break space before AM and PM.
const plain = (text: string) => text.replace(/\s/g, ' ')

describe('format', () => {
  const moment = new Date(2026, 8, 26, 14, 5)

  afterEach(() => {
    i18n.global.locale.value = 'en'
    setRegionalFormat({ region: null, hourCycle: 'h23' })
  })

  it('writes numbers the English way by default', () => {
    expect(formatNumber(12.345)).toBe('12.3')
    expect(formatNumber(12, 2)).toBe('12.00')
    expect(formatNumber(24852.4, 0, true)).toBe('24,852')
    expect(formatNumber(24852.4, 0)).toBe('24852')
  })

  it('writes numbers the Dutch way in Dutch', () => {
    i18n.global.locale.value = 'nl'

    expect(formatNumber(12.345)).toBe('12,3')
    expect(formatNumber(24852.4, 0, true)).toBe('24.852')
  })

  it('keeps numbers in the interface language wherever the browser is', () => {
    setRegionalFormat({ region: 'NL', hourCycle: 'h23' })

    expect(formatNumber(12.345)).toBe('12.3')
    expect(numberLocale()).toBe('en-US')
  })

  it('writes dates in the interface language and times on a 24-hour clock by default', () => {
    expect(formatDate(moment, { month: 'long' })).toBe('September')
    expect(formatTime(moment)).toBe('14:05')

    i18n.global.locale.value = 'nl'
    expect(formatDate(moment, { month: 'long' })).toBe('september')
    expect(formatTime(moment)).toBe('14:05')
  })

  it('writes the date in the order of the region', () => {
    setRegionalFormat({ region: 'NL', hourCycle: 'h23' })
    expect(formatDate(moment)).toBe('26/09/2026')
    expect(formatDateTime(moment)).toBe('26 Sept 2026, 14:05')

    setRegionalFormat({ region: 'US', hourCycle: 'h12' })
    expect(formatDate(moment)).toBe('9/26/2026')

    i18n.global.locale.value = 'nl'
    setRegionalFormat({ region: 'NL', hourCycle: 'h23' })
    expect(formatDate(moment)).toBe('26-9-2026')
  })

  it('falls back to international English, or the language itself, for a pair Intl lacks', () => {
    setRegionalFormat({ region: 'AQ', hourCycle: 'h23' })
    expect(formatDate(moment)).toBe('26/09/2026')

    i18n.global.locale.value = 'nl'
    setRegionalFormat({ region: 'US', hourCycle: 'h23' })
    expect(formatDate(moment)).toBe('26-9-2026')
  })

  it('shows every time on a 12-hour clock without a leading zero when that is chosen', () => {
    setRegionalFormat({ region: 'NL', hourCycle: 'h12' })

    expect(activeHourCycle()).toBe('h12')
    expect(plain(formatTime(new Date(2026, 8, 26, 9, 5)))).toBe('9:05 am')
    expect(plain(formatDateTime(moment))).toBe('26 Sept 2026, 2:05 pm')
    expect(plain(formatHour(7))).toBe('7:00 am')
  })

  it('writes the start of an hour on the chosen clock', () => {
    expect(formatHour(7)).toBe('07:00')
    expect(formatHour(17)).toBe('17:00')
  })

  it('puts a time of day kept as HH:mm on the chosen clock', () => {
    expect(formatTimeOfDay('07:30')).toBe('07:30')
    expect(formatTimeOfDay('18:05:00')).toBe('18:05')

    setRegionalFormat({ region: 'US', hourCycle: 'h12' })
    expect(plain(formatTimeOfDay('07:30'))).toBe('7:30 AM')
    expect(plain(formatTimeOfDay('00:00'))).toBe('12:00 AM')
  })

  it('shows a time of day it cannot read as it came', () => {
    expect(formatTimeOfDay('soon')).toBe('soon')
    expect(formatTimeOfDay('25:00')).toBe('25:00')
  })

  it('shows an example of either clock, whichever one is chosen', () => {
    expect(plain(clockExample('h12'))).toBe('2:05 PM')
    setRegionalFormat({ region: 'US', hourCycle: 'h12' })
    expect(clockExample('h23')).toBe('14:05')
  })

  it('names the halves of the day in the interface language', () => {
    expect(dayPeriodNames()).toEqual({ am: 'AM', pm: 'PM' })

    i18n.global.locale.value = 'nl'
    expect(dayPeriodNames()).toEqual({ am: 'a.m.', pm: 'p.m.' })
  })
})
