import { describe, it, expect } from 'vitest'
import type { Vehicle } from '@/services/vehicleApi'
import { titleWithCar, vehicleLabels } from '../vehicleLabels'

function car(id: number, vin: string, model: string | null, series: string | null = null): Vehicle {
  return {
    id,
    vin,
    model,
    series,
    createdAt: '2026-01-01T00:00:00Z',
    vehicleType: 'bev',
    hvBatteryCapacityKwh: null,
    supportsTargetSoc: false,
    hasPowerTailgate: false,
  }
}

describe('vehicleLabels', () => {
  it('names each car by its model', () => {
    const labels = vehicleLabels([
      car(1, 'VIN00000000000001', 'MG HS'),
      car(2, 'VIN00000000000002', 'MG4'),
    ])
    expect(labels.map((l) => l.label)).toEqual(['MG HS', 'MG4'])
  })

  it('adds the end of the VIN when two cars share a model', () => {
    const labels = vehicleLabels([
      car(1, 'VIN00000000AAAAAA', 'MG4'),
      car(2, 'VIN00000000BBBBBB', 'MG4'),
    ])
    expect(labels.map((l) => l.label)).toEqual(['MG4 (…AAAAAA)', 'MG4 (…BBBBBB)'])
  })

  it('falls back to the series, then the VIN', () => {
    const labels = vehicleLabels([
      car(1, 'VIN00000000000001', null, 'ZS EV'),
      car(2, 'VIN00000000000002', null),
    ])
    expect(labels.map((l) => l.label)).toEqual(['ZS EV', 'VIN00000000000002'])
  })
})

describe('titleWithCar', () => {
  it('puts the car in front of the title', () => {
    expect(titleWithCar('Engine started', 'MG4')).toBe('MG4 · Engine started')
  })

  it('leaves the title alone without a car', () => {
    expect(titleWithCar('Engine started', null)).toBe('Engine started')
  })
})
