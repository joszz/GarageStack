import { describe, it, expect } from 'vitest'
import { climateModeOf, climateModeOptions } from '../climate'

describe('climateModeOf', () => {
  it('reads off from climateOn, whatever mode was last named', () => {
    expect(climateModeOf({ climateOn: false, climateMode: 'front' })).toBe('off')
  })

  it('reads the named mode while climate is on', () => {
    expect(climateModeOf({ climateOn: true, climateMode: 'blowingonly' })).toBe('blowingonly')
    expect(climateModeOf({ climateOn: true, climateMode: 'front' })).toBe('front')
  })

  it('counts climate that is on without a mode, or with a stale off, as normal climate', () => {
    expect(climateModeOf({ climateOn: true, climateMode: null })).toBe('on')
    expect(climateModeOf({ climateOn: true, climateMode: 'off' })).toBe('on')
  })

  it('falls back to the mode alone, or null, when on/off is unknown', () => {
    expect(climateModeOf({ climateOn: null, climateMode: 'front' })).toBe('front')
    expect(climateModeOf({ climateOn: null, climateMode: null })).toBeNull()
  })
})

describe('climateModeOptions', () => {
  it('offers normal climate, fan only and front defrost, each labelled and with an icon', () => {
    const options = climateModeOptions((key) => key)

    expect(options.map((o) => o.value)).toEqual(['on', 'blowingonly', 'front'])
    expect(options.map((o) => o.label)).toEqual([
      'control.mode.on',
      'control.mode.blowingonly',
      'control.mode.front',
    ])
    expect(options.every((o) => o.icon)).toBe(true)
  })
})
