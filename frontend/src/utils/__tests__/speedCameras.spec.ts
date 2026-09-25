import { describe, it, expect } from 'vitest'
import { speedCameraKind, speedCameraLimit } from '@/utils/speedCameras'

describe('speedCameraKind', () => {
  it.each(['fixed', 'mobile', 'section', 'traffic_signals'])('recognises %s', (kind) => {
    expect(speedCameraKind({ speed_camera: kind })).toBe(kind)
  })

  it('reads the older camera:type key too', () => {
    expect(speedCameraKind({ 'camera:type': 'fixed' })).toBe('fixed')
  })

  it('prefers speed_camera over camera:type when a node carries both', () => {
    expect(speedCameraKind({ speed_camera: 'section', 'camera:type': 'fixed' })).toBe('section')
  })

  it.each(['FIXED', ' Section '])('normalises case and whitespace (%s)', (raw) => {
    expect(speedCameraKind({ speed_camera: raw })).toBe(raw.trim().toLowerCase())
  })

  it.each([
    ['a kind we have no name for', { speed_camera: 'gantry' }],
    ['an empty value', { speed_camera: '  ' }],
    ['no camera tag at all', { highway: 'speed_camera' }],
  ])('returns null for %s', (_label, tags) => {
    expect(speedCameraKind(tags)).toBeNull()
  })

  it('returns null without tags', () => {
    expect(speedCameraKind(null)).toBeNull()
    expect(speedCameraKind(undefined)).toBeNull()
  })
})

describe('speedCameraLimit', () => {
  it('reads a bare number as km/h, which is what OSM means by it', () => {
    expect(speedCameraLimit({ maxspeed: '50' })).toEqual({ value: 50, unit: 'km/h' })
  })

  it.each(['30 mph', '30mph', '30 MPH'])('keeps mph as mph (%s)', (raw) => {
    expect(speedCameraLimit({ maxspeed: raw })).toEqual({ value: 30, unit: 'mph' })
  })

  it.each(['50 km/h', '50km/h', '50 kmh', '50 kph'])('normalises %s to km/h', (raw) => {
    expect(speedCameraLimit({ maxspeed: raw })).toEqual({ value: 50, unit: 'km/h' })
  })

  it.each([
    ['an implicit zone reference', 'NL:urban'],
    ['no limit', 'none'],
    ['a signed variable limit', 'signals'],
    ['walking pace', 'walk'],
    ['a zero', '0'],
    ['a figure with a unit we do not know', '50 knots'],
  ])('returns null for %s', (_label, raw) => {
    expect(speedCameraLimit({ maxspeed: raw })).toBeNull()
  })

  it('returns null when the node has no maxspeed', () => {
    expect(speedCameraLimit({ speed_camera: 'fixed' })).toBeNull()
    expect(speedCameraLimit(null)).toBeNull()
  })
})
