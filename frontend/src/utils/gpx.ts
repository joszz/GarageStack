import type { TripPoint } from '@/services/vehicleApi'
import { localDateKey, pad2 } from '@/utils/dates'

/**
 * A trip as a GPX 1.1 track, for route planners, Strava-like tools and photo geotaggers. One
 * track with one segment, a point per fix as the trip log shows the route: the car's own fixes,
 * the phone's and the ones placed by hand alike. A point's speed, which GPX 1.1 has no element
 * for, goes in Garmin's TrackPointExtension in metres per second, which most tools read.
 */

const KMH_PER_MS = 3.6

function escapeXml(text: string): string {
  return text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
}

function trackPoint(point: TripPoint): string {
  const speed =
    point.speed !== null && Number.isFinite(point.speed)
      ? `<extensions><gpxtpx:TrackPointExtension><gpxtpx:speed>${(point.speed / KMH_PER_MS).toFixed(2)}</gpxtpx:speed></gpxtpx:TrackPointExtension></extensions>`
      : ''
  const time = new Date(point.recordedAt).toISOString()
  return `      <trkpt lat="${point.latitude.toFixed(6)}" lon="${point.longitude.toFixed(6)}"><time>${time}</time>${speed}</trkpt>`
}

/** The GPX document of one trip; `name` names the track, such as "Zwolle to Deventer". */
export function tripGpx(name: string, points: readonly TripPoint[]): string {
  const title = escapeXml(name)
  const start = points[0] ? `<time>${new Date(points[0].recordedAt).toISOString()}</time>` : ''
  return [
    '<?xml version="1.0" encoding="UTF-8"?>',
    '<gpx version="1.1" creator="GarageStack" xmlns="http://www.topografix.com/GPX/1/1"' +
      ' xmlns:gpxtpx="http://www.garmin.com/xmlschemas/TrackPointExtension/v2">',
    `  <metadata><name>${title}</name>${start}</metadata>`,
    '  <trk>',
    `    <name>${title}</name>`,
    '    <trkseg>',
    ...points.map(trackPoint),
    '    </trkseg>',
    '  </trk>',
    '</gpx>',
    '',
  ].join('\n')
}

/** "trip-2026-09-24-1432.gpx": the trip's local start, so a folder of them sorts by time. */
export function tripGpxFileName(startedAt: string): string {
  const start = new Date(startedAt)
  return `trip-${localDateKey(start)}-${pad2(start.getHours())}${pad2(start.getMinutes())}.gpx`
}
