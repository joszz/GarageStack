import { describe, it, expect } from 'vitest'
import { formatAppVersion } from '@/utils/appVersion'

describe('formatAppVersion', () => {
  it('prefixes a release version with "v"', () => {
    expect(formatAppVersion('0.5.0')).toBe('v0.5.0')
  })

  it('keeps prerelease identifiers intact', () => {
    expect(formatAppVersion('0.5.1-preview.0.12')).toBe('v0.5.1-preview.0.12')
  })

  it('trims surrounding whitespace', () => {
    expect(formatAppVersion(' 1.2.3\n')).toBe('v1.2.3')
  })

  it.each([undefined, '', '   '])('returns null when no version was injected (%j)', (version) => {
    expect(formatAppVersion(version)).toBeNull()
  })
})
