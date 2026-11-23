import { describe, it, expect } from 'vitest'
import { tripGpx, tripGpxFileName } from '../gpx'

const points = [
  { recordedAt: '2026-09-24T12:32:00Z', latitude: 52.5123, longitude: 6.0921, speed: 0 },
  { recordedAt: '2026-09-24T12:40:00Z', latitude: 52.4, longitude: 6.1, speed: 90 },
  {
    recordedAt: '2026-09-24T12:50:00Z',
    latitude: 52.3,
    longitude: 6.12,
    speed: null,
    source: 'phone' as const,
  },
]

describe('tripGpx', () => {
  it('writes one track with a point per fix, its time in UTC and its speed in m/s', () => {
    const gpx = tripGpx('Zwolle to Deventer', points)

    expect(gpx).toMatch(/^<\?xml version="1.0" encoding="UTF-8"\?>\n<gpx version="1.1"/)
    expect(gpx).toContain(
      '<metadata><name>Zwolle to Deventer</name><time>2026-09-24T12:32:00.000Z</time></metadata>',
    )
    expect(gpx.match(/<trkpt /g)).toHaveLength(3)
    expect(gpx).toContain(
      '<trkpt lat="52.400000" lon="6.100000"><time>2026-09-24T12:40:00.000Z</time><extensions><gpxtpx:TrackPointExtension><gpxtpx:speed>25.00</gpxtpx:speed></gpxtpx:TrackPointExtension></extensions></trkpt>',
    )
    // The phone's fix carries no speed, and the track says nothing about one.
    expect(gpx).toContain(
      '<trkpt lat="52.300000" lon="6.120000"><time>2026-09-24T12:50:00.000Z</time></trkpt>',
    )
  })

  it('escapes what the addresses hold', () => {
    expect(tripGpx('Kade & <Haven>', points)).toContain('<name>Kade &amp; &lt;Haven&gt;</name>')
  })

  it('parses as XML', () => {
    const doc = new DOMParser().parseFromString(tripGpx('A to B', points), 'application/xml')

    expect(doc.getElementsByTagName('parsererror')).toHaveLength(0)
    expect(doc.getElementsByTagName('trkpt')).toHaveLength(3)
  })
})

describe('tripGpxFileName', () => {
  it("is named after the trip's local start", () => {
    expect(tripGpxFileName(new Date(2026, 8, 24, 14, 32).toISOString())).toBe(
      'trip-2026-09-24-1432.gpx',
    )
  })
})
