import { describe, it, expect } from 'vitest'
import { buildCarMarkerIcon } from '@/utils/mapCarIcon'

describe('buildCarMarkerIcon', () => {
  // Regression guard: the rotation used to sit in a style attribute inside an HTML string,
  // which the Content-Security-Policy (style-src-attr 'none') blocks once Leaflet assigns it
  // with innerHTML, so the marker never turned. Building the element and setting the property
  // goes through the CSSOM instead.
  it('returns an element whose rotation is set as a style property', () => {
    const icon = buildCarMarkerIcon(150)
    const html = icon.options.html

    expect(html).toBeInstanceOf(HTMLElement)
    expect((html as HTMLElement).style.transform).toBe('rotate(150deg)')
    expect((html as HTMLElement).className).toContain('trip-marker--active')
  })

  it('falls back to north when the heading is not a number', () => {
    const icon = buildCarMarkerIcon(Number.NaN)

    expect((icon.options.html as HTMLElement).style.transform).toBe('rotate(0deg)')
  })

  it('renders the car silhouette inside the marker', () => {
    const icon = buildCarMarkerIcon(0)

    expect(
      (icon.options.html as HTMLElement).querySelector('svg.trip-marker-car-svg'),
    ).not.toBeNull()
  })
})
