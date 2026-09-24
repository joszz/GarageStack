/**
 * Evenly thins a list down to `max` entries, always keeping the first and the last. Used wherever
 * a trip's fixes are denser than what is about to consume them: one Leaflet layer per speed
 * segment, a heatmap canvas, or a matching request the API caps.
 */
export function downsample<T>(items: T[], max: number): T[] {
  if (items.length <= max) return items

  const stride = items.length / max
  const sampled: T[] = []
  for (let i = 0; i < max; i++) {
    sampled.push(items[Math.floor(i * stride)]!)
  }

  const last = items[items.length - 1]!
  if (sampled[sampled.length - 1] !== last) sampled.push(last)
  return sampled
}
