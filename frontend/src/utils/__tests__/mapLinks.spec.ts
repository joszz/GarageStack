import { describe, it, expect } from 'vitest'
import {
  appleDirectionsUrl,
  formatCoordinates,
  geoUri,
  googleDirectionsUrl,
  osmUrl,
} from '../mapLinks'

describe('directions links', () => {
  it('opens walking directions in Google Maps without specifying an origin', () => {
    const url = new URL(googleDirectionsUrl(52.3676, 4.9041))

    expect(url.origin + url.pathname).toBe('https://www.google.com/maps/dir/')
    expect(Object.fromEntries(url.searchParams)).toEqual({
      api: '1',
      destination: '52.367600,4.904100',
      travelmode: 'walking',
    })
  })

  it('opens walking directions in Apple Maps without specifying an origin', () => {
    const url = new URL(appleDirectionsUrl(-33.8688, -70.6693))

    expect(url.origin + url.pathname).toBe('https://maps.apple.com/directions')
    expect(Object.fromEntries(url.searchParams)).toEqual({
      destination: '-33.868800,-70.669300',
      mode: 'walking',
    })
  })

  it('keeps zero and boundary coordinates in a standard, locale-independent form', () => {
    expect(formatCoordinates(0, 0)).toBe('0.000000,0.000000')
    expect(formatCoordinates(-90, 180)).toBe('-90.000000,180.000000')
    expect(new URL(googleDirectionsUrl(0, 0)).searchParams.get('destination')).toBe(
      '0.000000,0.000000',
    )
  })
})

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
