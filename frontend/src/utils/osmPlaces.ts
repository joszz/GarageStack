/**
 * The places a driver looks up besides fuel: car parks, car washes, air pumps and garages, each an
 * OpenStreetMap layer of its own. What their popups say comes from their tags, which are
 * crowd-edited and often sparse, so every line is left out when its tag is missing or unclear.
 */

export const OSM_PLACE_LAYERS = ['parking', 'car_wash', 'compressed_air', 'car_repair'] as const

export type OsmPlaceLayer = (typeof OSM_PLACE_LAYERS)[number]

/** One popup line: a translation key with what it names, or text OSM holds as it is. */
export type PlaceLine = { key: string; params?: Record<string, string | number> } | { text: string }

type Tags = Record<string, string> | null | undefined

function yes(value: string | undefined): boolean {
  return value === 'yes'
}

// A fee the place says it charges, or says it does not.
function feeLine(tags: Tags): PlaceLine | null {
  if (yes(tags?.fee)) return { key: 'poi.fee.paid' }
  if (tags?.fee === 'no') return { key: 'poi.fee.free' }
  return null
}

function textLine(value: string | undefined): PlaceLine | null {
  const text = value?.trim()
  return text ? { text } : null
}

function parkingLines(tags: Tags): (PlaceLine | null)[] {
  const kind = tags?.parking
  const capacity = Number(tags?.capacity)
  return [
    kind === 'multi-storey' || kind === 'underground' || kind === 'surface'
      ? { key: `poi.parking.kind.${kind === 'multi-storey' ? 'multiStorey' : kind}` }
      : null,
    tags?.park_ride && tags.park_ride !== 'no' ? { key: 'poi.parking.parkRide' } : null,
    feeLine(tags),
    // Written as the sign has it: "2.1", "2.1 m" or 6'8" all occur.
    tags?.maxheight && tags.maxheight !== 'none'
      ? { key: 'poi.parking.maxHeight', params: { height: tags.maxheight } }
      : null,
    Number.isInteger(capacity) && capacity > 0
      ? { key: 'poi.parking.capacity', params: { count: capacity } }
      : null,
  ]
}

function carWashLines(tags: Tags): (PlaceLine | null)[] {
  return [
    textLine(tags?.brand ?? tags?.operator),
    yes(tags?.self_service) ? { key: 'poi.carWash.selfService' } : null,
    yes(tags?.automated) ? { key: 'poi.carWash.automated' } : null,
  ]
}

function airLines(tags: Tags): (PlaceLine | null)[] {
  return [
    tags?.amenity === 'fuel' ? { key: 'poi.air.atFuelStation' } : null,
    textLine(tags?.brand ?? tags?.operator),
    feeLine(tags),
  ]
}

function repairLines(tags: Tags): (PlaceLine | null)[] {
  return [
    tags?.shop === 'tyres' ? { key: 'poi.repair.tyres' } : null,
    textLine(tags?.brand ?? tags?.operator),
  ]
}

const LINES: Record<OsmPlaceLayer, (tags: Tags) => (PlaceLine | null)[]> = {
  parking: parkingLines,
  car_wash: carWashLines,
  compressed_air: airLines,
  car_repair: repairLines,
}

/** What a place's popup says under its name, in order, with the opening hours last. */
export function placeLines(layer: OsmPlaceLayer, tags: Tags): PlaceLine[] {
  return [...LINES[layer](tags), textLine(tags?.opening_hours)].filter(
    (line): line is PlaceLine => line !== null,
  )
}
