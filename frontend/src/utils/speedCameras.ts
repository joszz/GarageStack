// What a `highway=speed_camera` node says about itself, read out of its OSM tags. Crowd-edited
// data, so every value is treated as untrusted text: only the shapes below are recognised and
// anything else is reported as unknown rather than passed through to the popup.

/** Camera kinds worth naming in a popup. Ids double as i18n keys (`trips.speedCameraTypes.*`). */
export const SPEED_CAMERA_KINDS = ['fixed', 'mobile', 'section', 'traffic_signals'] as const

export type SpeedCameraKind = (typeof SPEED_CAMERA_KINDS)[number]

/** A limit the camera enforces. The unit is OSM's own: km/h unless the tag says mph. */
export interface SpeedCameraLimit {
  value: number
  unit: 'km/h' | 'mph'
}

function isKind(value: string): value is SpeedCameraKind {
  return (SPEED_CAMERA_KINDS as readonly string[]).includes(value)
}

/**
 * The kind of camera, or null when it is untagged or tagged with something we have no name for.
 * OSM's own key is `speed_camera`; `camera:type` carries the same values on older data.
 */
export function speedCameraKind(
  tags: Record<string, string> | null | undefined,
): SpeedCameraKind | null {
  const raw = (tags?.['speed_camera'] ?? tags?.['camera:type'])?.trim().toLowerCase()
  if (!raw) return null
  return isKind(raw) ? raw : null
}

/**
 * The enforced limit, or null when there is none to show. `maxspeed` is free text in OSM: a bare
 * number means km/h, "30 mph" and "50 km/h" spell their unit out, and the rest ("none", "signals",
 * "NL:urban" and other implicit-zone references) name no figure a popup could state.
 */
export function speedCameraLimit(
  tags: Record<string, string> | null | undefined,
): SpeedCameraLimit | null {
  const raw = tags?.['maxspeed']?.trim().toLowerCase()
  if (!raw) return null

  const match = /^(\d{1,3})(?:\s*(mph|km\/h|kmh|kph))?$/.exec(raw)
  if (!match) return null

  const value = Number(match[1])
  if (value <= 0) return null

  return { value, unit: match[2] === 'mph' ? 'mph' : 'km/h' }
}
