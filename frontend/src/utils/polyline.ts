/**
 * Decodes the encoded-polyline format the map API answers snapped trips in: a line's coordinates
 * as one string of deltas instead of a JSON array per point, which keeps a few thousand vertices
 * a few kilobytes on the wire. Six decimals (about 10 cm) is what the API encodes at.
 *
 * Forgiving on purpose: a truncated string yields the vertices that were complete rather than
 * throwing, so a half-received line still draws.
 */
export function decodePolyline(encoded: string | null, precision = 6): [number, number][] {
  if (!encoded) return []

  const source = encoded
  const factor = Math.pow(10, precision)
  const coordinates: [number, number][] = []
  let index = 0
  let lat = 0
  let lng = 0

  function decodeValue(): number | null {
    let result = 0
    let shift = 0
    let chunk: number
    do {
      if (index >= source.length) return null
      chunk = source.charCodeAt(index++) - 63
      if (chunk < 0) return null
      result |= (chunk & 0x1f) << shift
      shift += 5
      // A value never needs more than six chunks; anything longer is a malformed string rather
      // than a very large number, and would otherwise shift past what a 32-bit operand holds.
      if (shift > 30) return null
    } while (chunk >= 0x20)

    // Negative numbers are stored one's-complemented and shifted left by one.
    return result & 1 ? ~(result >> 1) : result >> 1
  }

  while (index < source.length) {
    const latDelta = decodeValue()
    if (latDelta === null) break
    const lngDelta = decodeValue()
    if (lngDelta === null) break
    lat += latDelta
    lng += lngDelta
    coordinates.push([lat / factor, lng / factor])
  }

  return coordinates
}
