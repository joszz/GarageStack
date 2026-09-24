import { describe, it, expect, afterEach, vi } from 'vitest'
import { geoUri, osmUrl, prefersMapApp, mapAppLink } from '../mapLinks'

describe('geoUri', () => {
  it('names the point in the query so the pin arrives labelled', () => {
    expect(geoUri(52.3676, 4.9041, 'MG HS')).toBe(
      'geo:52.367600,4.904100?q=52.367600,4.904100(MG%20HS)',
    )
  })

  it('leaves out the label when there is none', () => {
    expect(geoUri(52.3676, 4.9041)).toBe('geo:52.367600,4.904100?q=52.367600,4.904100')
  })

  it('escapes a label that would otherwise close the query early', () => {
    expect(geoUri(1, 2, 'MG (HS) & co')).toContain('q=1.000000,2.000000(MG%20(HS)%20%26%20co)')
  })

  it('keeps southern and western coordinates negative', () => {
    expect(geoUri(-33.8688, -70.6693)).toBe('geo:-33.868800,-70.669300?q=-33.868800,-70.669300')
  })
})

describe('osmUrl', () => {
  it('puts a marker on the point and centres the view on it', () => {
    expect(osmUrl(52.3676, 4.9041)).toBe(
      'https://www.openstreetmap.org/?mlat=52.367600&mlon=4.904100#map=17/52.367600/4.904100',
    )
  })
})

describe('prefersMapApp', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('is true on a device with a coarse pointer', () => {
    vi.stubGlobal('window', { matchMedia: () => ({ matches: true }) })
    expect(prefersMapApp()).toBe(true)
  })

  it('is false on a desktop, where a geo: link leads nowhere', () => {
    vi.stubGlobal('window', { matchMedia: () => ({ matches: false }) })
    expect(prefersMapApp()).toBe(false)
  })

  it('is false where matchMedia is missing rather than throwing', () => {
    vi.stubGlobal('window', {})
    expect(prefersMapApp()).toBe(false)
  })
})

describe('mapAppLink', () => {
  it('hands a phone over to its map app, without opening a tab for it', () => {
    expect(mapAppLink(52.3676, 4.9041, 'MG HS', true)).toEqual({
      href: 'geo:52.367600,4.904100?q=52.367600,4.904100(MG%20HS)',
      external: false,
    })
  })

  it('sends a desktop to openstreetmap.org in a new tab', () => {
    expect(mapAppLink(52.3676, 4.9041, 'MG HS', false)).toEqual({
      href: 'https://www.openstreetmap.org/?mlat=52.367600&mlon=4.904100#map=17/52.367600/4.904100',
      external: true,
    })
  })
})
