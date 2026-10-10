import { describe, it, expect, vi } from 'vitest'
import { isRegion, REGIONS, regionHourCycle, regionName } from '@/utils/region'

// test-setup pins detectRegion for every other test; this file tests the real one.
const { detectRegion } = await vi.importActual<typeof import('@/utils/region')>('@/utils/region')

describe('detectRegion', () => {
  it('goes by the time zone before the language, which is often a default', () => {
    expect(detectRegion({ timeZone: 'Europe/Amsterdam', languages: ['en-US', 'en'] })).toBe('NL')
    expect(detectRegion({ timeZone: 'Europe/London', languages: ['en-US'] })).toBe('GB')
    expect(detectRegion({ timeZone: 'America/Chicago', languages: ['nl-NL'] })).toBe('US')
  })

  it('knows a renamed zone under both of its names', () => {
    expect(detectRegion({ timeZone: 'Asia/Calcutta', languages: [] })).toBe('IN')
    expect(detectRegion({ timeZone: 'Asia/Kolkata', languages: [] })).toBe('IN')
    expect(detectRegion({ timeZone: 'Europe/Kyiv', languages: [] })).toBe('UA')
    expect(detectRegion({ timeZone: 'America/Argentina/Buenos_Aires', languages: [] })).toBe('AR')
  })

  it('takes the first language that names a region when the zone says nothing', () => {
    expect(detectRegion({ timeZone: 'UTC', languages: ['nl', 'en-GB', 'en-US'] })).toBe('GB')
    expect(detectRegion({ timeZone: undefined, languages: ['de-AT'] })).toBe('AT')
  })

  it('falls back to where the first language is most spoken, and then to the United States', () => {
    expect(detectRegion({ timeZone: 'Etc/GMT+2', languages: ['nl'] })).toBe('NL')
    expect(detectRegion({ timeZone: undefined, languages: ['not a language tag'] })).toBe('US')
    expect(detectRegion({ timeZone: undefined, languages: [] })).toBe('US')
  })
})

describe('regionHourCycle', () => {
  it('reads the clock the region lives by', () => {
    expect(regionHourCycle('NL')).toBe('h23')
    expect(regionHourCycle('GB')).toBe('h23')
    expect(regionHourCycle('DE')).toBe('h23')
    expect(regionHourCycle('US')).toBe('h12')
    expect(regionHourCycle('AU')).toBe('h12')
    expect(regionHourCycle('IN')).toBe('h12')
  })
})

describe('regions', () => {
  it('lists the current regions only, not codes that were retired', () => {
    expect(REGIONS).toContain('NL')
    expect(REGIONS).toContain('US')
    expect(REGIONS).not.toContain('UK')
    expect(REGIONS).not.toContain('SU')
  })

  it('recognises a region code and nothing else', () => {
    expect(isRegion('NL')).toBe(true)
    expect(isRegion('XX')).toBe(false)
    expect(isRegion('constructor')).toBe(false)
    expect(isRegion(undefined)).toBe(false)
  })

  it('names a region in the interface language', () => {
    expect(regionName('NL', 'en')).toBe('Netherlands')
    expect(regionName('NL', 'nl')).toBe('Nederland')
  })
})
