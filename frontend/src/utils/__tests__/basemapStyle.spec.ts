import { describe, it, expect } from 'vitest'
import { boostLabelContrast, localizeStyleLabels, type MapStyle } from '../basemapStyle'

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

describe('boostLabelContrast', () => {
  const darkBackground = {
    id: 'background',
    type: 'background',
    paint: { 'background-color': 'rgb(12,12,12)' },
  }
  const lightBackground = {
    id: 'background',
    type: 'background',
    paint: { 'background-color': '#fff' },
  }

  function label(id: string, paint: Record<string, unknown>) {
    return { id, type: 'symbol', layout: { 'text-field': ['get', 'name'] }, paint }
  }

  it('lifts labels that are too dim to read on a dark basemap', () => {
    // The shipped dark style's place labels: 3.4:1 against its own background.
    const style = styleWith([
      darkBackground,
      label('place_city', { 'text-color': 'rgb(101,101,101)' }),
    ])

    const boosted = boostLabelContrast(style)

    expect(boosted.layers![1]!.paint).toEqual({
      'text-color': '#e2e8f0',
      'text-halo-color': 'rgba(12, 12, 12, 0.85)',
      'text-halo-width': 1,
    })
  })

  it('rescues a label left black by the light style it was copied from', () => {
    // water_name in the dark style: black text with a dark grey halo, invisible on black.
    const style = styleWith([
      darkBackground,
      label('water_name', {
        'text-color': 'hsla(0,0%,0%,0.7)',
        'text-halo-color': 'hsl(0,0%,27%)',
      }),
    ])

    const boosted = boostLabelContrast(style)

    expect(boosted.layers![1]!.paint!['text-color']).toBe('#e2e8f0')
    expect(boosted.layers![1]!.paint!['text-halo-color']).toBe('rgba(12, 12, 12, 0.85)')
  })

  it('keeps a wider halo the style already asked for', () => {
    const style = styleWith([
      darkBackground,
      label('place_country', { 'text-color': 'rgb(101,101,101)', 'text-halo-width': 1.4 }),
    ])

    expect(boostLabelContrast(style).layers![1]!.paint!['text-halo-width']).toBe(1.4)
  })

  it('leaves labels that already read well alone, in either theme', () => {
    const onDark = label('readable_dark', { 'text-color': '#e8eaed' })
    const onLight = label('readable_light', { 'text-color': '#222' })

    expect(boostLabelContrast(styleWith([darkBackground, onDark])).layers![1]).toBe(onDark)
    expect(boostLabelContrast(styleWith([lightBackground, onLight])).layers![1]).toBe(onLight)
  })

  it('darkens an unreadable label on a light basemap instead of lightening it', () => {
    const style = styleWith([lightBackground, label('washed_out', { 'text-color': '#f0f0f0' })])

    expect(boostLabelContrast(style).layers![1]!.paint!['text-color']).toBe('#1a202c')
  })

  it('treats a label with no colour of its own as the spec default, which is black', () => {
    const style = styleWith([darkBackground, label('no_colour', {})])

    expect(boostLabelContrast(style).layers![1]!.paint!['text-color']).toBe('#e2e8f0')
  })

  it('leaves a colour that varies by zoom or feature to the style', () => {
    const expression = label('zoom_coloured', {
      'text-color': ['interpolate', ['linear'], ['zoom'], 5, '#111', 12, '#999'],
    })

    expect(boostLabelContrast(styleWith([darkBackground, expression])).layers![1]).toBe(expression)
  })

  it('leaves layers that draw no text alone', () => {
    const icon = { id: 'poi_icon', type: 'symbol', layout: { 'icon-image': 'marker' } }
    const road = { id: 'road', type: 'line', paint: { 'line-color': '#333' } }
    const style = styleWith([darkBackground, icon, road])

    const boosted = boostLabelContrast(style)

    expect(boosted.layers![1]).toBe(icon)
    expect(boosted.layers![2]).toBe(road)
  })

  it('does nothing when the style has no background to measure against', () => {
    const style = styleWith([label('place_city', { 'text-color': 'rgb(101,101,101)' })])

    expect(boostLabelContrast(style)).toBe(style)
  })
})
