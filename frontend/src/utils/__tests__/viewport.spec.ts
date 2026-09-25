import { describe, it, expect, afterEach, vi } from 'vitest'
import { isPhoneViewport, PHONE_MEDIA_QUERY } from '../viewport'

describe('isPhoneViewport', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('asks for the same breakpoint the stylesheets use', () => {
    const matchMedia = vi.fn<(query: string) => { matches: boolean }>(() => ({ matches: true }))
    vi.stubGlobal('window', { matchMedia })
    expect(isPhoneViewport()).toBe(true)
    expect(matchMedia).toHaveBeenCalledWith(PHONE_MEDIA_QUERY)
    expect(PHONE_MEDIA_QUERY).toBe('(width <= 767px)')
  })

  it('is false on a wider viewport', () => {
    vi.stubGlobal('window', { matchMedia: () => ({ matches: false }) })
    expect(isPhoneViewport()).toBe(false)
  })

  it('is false where matchMedia is missing rather than throwing', () => {
    vi.stubGlobal('window', {})
    expect(isPhoneViewport()).toBe(false)
  })
})
