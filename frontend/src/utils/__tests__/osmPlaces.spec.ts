import { describe, it, expect } from 'vitest'
import { placeLines } from '../osmPlaces'

describe('placeLines', () => {
  it('describes a car park by its kind, fee, height limit and spaces, hours last', () => {
    expect(
      placeLines('parking', {
        parking: 'multi-storey',
        fee: 'yes',
        maxheight: '2.1',
        capacity: '420',
        opening_hours: '24/7',
      }),
    ).toEqual([
      { key: 'poi.parking.kind.multiStorey' },
      { key: 'poi.fee.paid' },
      { key: 'poi.parking.maxHeight', params: { height: '2.1' } },
      { key: 'poi.parking.capacity', params: { count: 420 } },
      { text: '24/7' },
    ])
  })

  it('names park and ride, and leaves out what it cannot read', () => {
    expect(
      placeLines('parking', { park_ride: 'train', fee: 'no', capacity: 'many', maxheight: 'none' }),
    ).toEqual([{ key: 'poi.parking.parkRide' }, { key: 'poi.fee.free' }])
  })

  it('says how a car wash works', () => {
    expect(placeLines('car_wash', { brand: 'IMO', self_service: 'yes', automated: 'yes' })).toEqual(
      [{ text: 'IMO' }, { key: 'poi.carWash.selfService' }, { key: 'poi.carWash.automated' }],
    )
  })

  it('marks an air pump that is mapped on its fuel station', () => {
    expect(placeLines('compressed_air', { amenity: 'fuel', brand: 'Shell', fee: 'no' })).toEqual([
      { key: 'poi.air.atFuelStation' },
      { text: 'Shell' },
      { key: 'poi.fee.free' },
    ])
  })

  it('tells a tyre shop from a garage, with its brand', () => {
    expect(placeLines('car_repair', { shop: 'tyres', brand: 'Euromaster' })).toEqual([
      { key: 'poi.repair.tyres' },
      { text: 'Euromaster' },
    ])
    expect(placeLines('car_repair', { shop: 'car_repair', brand: 'MG' })).toEqual([{ text: 'MG' }])
  })

  it('says nothing for a place without tags', () => {
    expect(placeLines('car_wash', null)).toEqual([])
  })
})
