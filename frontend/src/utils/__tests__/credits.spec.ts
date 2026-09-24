import { describe, it, expect } from 'vitest'
import { OSM_ATTRIBUTION, VECTOR_ATTRIBUTION, OCM_ATTRIBUTION } from '../credits'

function textOf(html: string): string {
  const el = document.createElement('div')
  el.innerHTML = html
  return el.textContent?.replace(/\s+/g, ' ').trim() ?? ''
}

function withoutOptional(html: string): string {
  const el = document.createElement('div')
  el.innerHTML = html
  el.querySelector('.map-credit-optional')?.remove()
  return el.textContent?.replace(/\s+/g, ' ').trim() ?? ''
}

describe('credits', () => {
  it('links the OpenStreetMap credit to the licence, as ODbL asks', () => {
    expect(OSM_ATTRIBUTION).toContain('https://www.openstreetmap.org/copyright')
    expect(textOf(OSM_ATTRIBUTION)).toBe('© OpenStreetMap contributors')
  })

  it('credits all three sources behind the vector basemap', () => {
    expect(textOf(VECTOR_ATTRIBUTION)).toBe(
      'OpenFreeMap · © OpenMapTiles · © OpenStreetMap contributors',
    )
    for (const href of ['openfreemap.org', 'openmaptiles.org', 'openstreetmap.org/copyright']) {
      expect(VECTOR_ATTRIBUTION).toContain(href)
    }
  })

  it('opens every credit in its own tab, away from the app', () => {
    for (const credit of [OSM_ATTRIBUTION, VECTOR_ATTRIBUTION, OCM_ATTRIBUTION]) {
      const links = [...credit.matchAll(/<a /g)]
      expect(links.length).toBeGreaterThan(0)
      expect([...credit.matchAll(/rel="noreferrer"/g)]).toHaveLength(links.length)
    }
  })

  it('marks only the part OpenFreeMap says may be dropped, separator included', () => {
    // The phone stylesheet hides .map-credit-optional to keep the credits on one row. Dropping it
    // has to leave the two required credits reading correctly, with no separator left dangling.
    expect(withoutOptional(VECTOR_ATTRIBUTION)).toBe(
      '© OpenMapTiles · © OpenStreetMap contributors',
    )
  })

  it('credits Open Charge Map with a link, as CC BY asks', () => {
    expect(OCM_ATTRIBUTION).toContain('https://openchargemap.org')
    expect(textOf(OCM_ATTRIBUTION)).toBe('Open Charge Map')
  })
})
