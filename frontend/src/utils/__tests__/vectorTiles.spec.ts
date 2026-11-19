import { describe, expect, it } from 'vitest'
import { basemapOrigins, isMapRendererPath } from '../vectorTiles'

const OWN = 'https://garage.example'

describe('basemapOrigins', () => {
  it('names each style host once', () => {
    const origins = basemapOrigins(
      [
        'https://tiles.openfreemap.org/styles/dark',
        'https://tiles.openfreemap.org/styles/positron',
        'https://maps.example.net/style.json',
      ],
      OWN,
    )

    expect([...origins]).toEqual(['https://tiles.openfreemap.org', 'https://maps.example.net'])
  })

  // Keeping the app's own host would keep everything the app fetches from itself.
  it('leaves out a style served by the app itself', () => {
    expect(basemapOrigins(['/styles/dark.json', `${OWN}/styles/light.json`], OWN).size).toBe(0)
  })
})

describe('isMapRendererPath', () => {
  it.each([
    ['/assets/maplibreLayer-4n2PAy1z.js', true],
    ['/assets/maplibreLayer-qvkd0SPb.css', true],
    ['/assets/maplibre-gl-worker-Dlv-oLtU.js', true],
    ['/assets/index-abc.js', false],
    ['/maplibre', false],
  ])('%s: %s', (path, expected) => {
    expect(isMapRendererPath(path)).toBe(expected)
  })
})
