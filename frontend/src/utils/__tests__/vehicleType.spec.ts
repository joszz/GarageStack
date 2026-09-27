import { describe, it, expect } from 'vitest'
import type { VehicleType } from '@/stores/vehicle'
import { burnsFuel, mayBurnFuel, mayPlugIn, plugsIn } from '@/utils/vehicleType'

describe('drivetrain predicates', () => {
  it.each<[VehicleType, boolean, boolean, boolean, boolean]>([
    // type       plugsIn mayPlugIn burnsFuel mayBurnFuel
    ['hev', false, false, true, true],
    ['phev', true, true, true, true],
    ['bev', true, true, false, false],
    ['unknown', false, true, false, true],
  ])('%s', (type, plugs, mayPlug, burns, mayBurn) => {
    expect(plugsIn(type)).toBe(plugs)
    expect(mayPlugIn(type)).toBe(mayPlug)
    expect(burnsFuel(type)).toBe(burns)
    expect(mayBurnFuel(type)).toBe(mayBurn)
  })
})
