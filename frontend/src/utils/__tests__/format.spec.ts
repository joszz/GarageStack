import { describe, it, expect, afterEach } from 'vitest'
import { i18n } from '@/i18n'
import { formatDate, formatNumber, formatTime } from '@/utils/format'

describe('format', () => {
  afterEach(() => {
    i18n.global.locale.value = 'en'
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

  it('writes dates and times in the interface language', () => {
    const moment = new Date(2026, 8, 26, 14, 5)

    expect(formatDate(moment, { month: 'long' })).toBe('September')
    expect(formatTime(moment, { hour: '2-digit', minute: '2-digit' })).toBe('02:05 PM')

    i18n.global.locale.value = 'nl'
    expect(formatDate(moment, { month: 'long' })).toBe('september')
    expect(formatTime(moment, { hour: '2-digit', minute: '2-digit' })).toBe('14:05')
  })
})
