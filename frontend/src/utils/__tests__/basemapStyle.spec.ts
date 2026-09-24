import { describe, it, expect } from 'vitest'
import { localizeStyleLabels, type MapStyle } from '../basemapStyle'

// The shape OpenFreeMap's styles use: a case expression that falls back to the local name.
const openMapTilesTextField = [
  'case',
  ['has', 'name:nonlatin'],
  ['concat', ['get', 'name:latin'], '\n', ['get', 'name:nonlatin']],
  ['coalesce', ['get', 'name_en'], ['get', 'name']],
]

function styleWith(layers: MapStyle['layers']): MapStyle {
  return { version: 8, name: 'test', layers }
}

describe('localizeStyleLabels', () => {
  it('prefers the name in the UI language and keeps the original as fallback', () => {
    const style = styleWith([
      { id: 'place_label', type: 'symbol', layout: { 'text-field': openMapTilesTextField } },
    ])

    const localized = localizeStyleLabels(style, 'nl')

    expect(localized.layers![0]!.layout!['text-field']).toEqual([
      'coalesce',
      ['get', 'name:nl'],
      openMapTilesTextField,
    ])
  })

  it('leaves layers that label something other than a name alone', () => {
    // Road shields label by ref ("A28"), which has no translation to fall back to.
    const shield = {
      id: 'road_shield',
      type: 'symbol',
      layout: { 'text-field': ['to-string', ['get', 'ref']] },
    }
    const line = { id: 'water', type: 'line', paint: { 'line-color': '#000' } }
    const style = styleWith([shield, line])

    const localized = localizeStyleLabels(style, 'en')

    expect(localized.layers![0]).toBe(shield)
    expect(localized.layers![1]).toBe(line)
  })

  it('leaves the legacy string form alone, which cannot hold a nested expression', () => {
    const layer = { id: 'place_label', type: 'symbol', layout: { 'text-field': '{name}' } }

    const localized = localizeStyleLabels(styleWith([layer]), 'nl')

    expect(localized.layers![0]).toBe(layer)
  })

  it('does not touch the style it was given, so the cached original stays reusable', () => {
    const layer = {
      id: 'place_label',
      type: 'symbol',
      layout: { 'text-field': openMapTilesTextField },
    }
    const style = styleWith([layer])

    const dutch = localizeStyleLabels(style, 'nl')
    const english = localizeStyleLabels(style, 'en')

    expect(layer.layout['text-field']).toBe(openMapTilesTextField)
    expect(dutch.layers![0]!.layout!['text-field']).toEqual([
      'coalesce',
      ['get', 'name:nl'],
      openMapTilesTextField,
    ])
    expect(english.layers![0]!.layout!['text-field']).toEqual([
      'coalesce',
      ['get', 'name:en'],
      openMapTilesTextField,
    ])
  })

  it('survives a style document without layers', () => {
    const style: MapStyle = { version: 8 }
    expect(localizeStyleLabels(style, 'nl')).toBe(style)
  })
})
