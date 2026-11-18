import { describe, it, expect } from 'vitest'
import { tripEndIcon, tripStartIcon } from '../tripMarkers'

describe('trip markers', () => {
  it('centres the pulsing start dot on where the trip started', () => {
    const { options } = tripStartIcon()

    expect(options.html).toContain('trip-marker--start')
    expect(options.iconSize).toEqual([16, 16])
    expect(options.iconAnchor).toEqual([8, 8])
  })

  it('stands the foot of the flag pole on where the trip ended', () => {
    const { options } = tripEndIcon()

    expect(options.html).toContain('trip-flag-pole')
    expect(options.html).toContain('trip-flag-flag')
    expect(options.iconSize).toEqual([20, 32])
    expect(options.iconAnchor).toEqual([2, 32])
  })
})
