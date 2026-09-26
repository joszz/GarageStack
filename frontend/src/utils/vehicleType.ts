import type { VehicleType } from '@/stores/vehicle'

// What a drivetrain can do, for deciding what to show. Each question comes in two strengths:
// "does" answers only for a car known to have it, "may" also answers yes while the type is still
// 'unknown', for the places where briefly showing too much beats hiding what the car does have.

/** Plugs in to charge: a plug-in hybrid or a BEV. */
export function plugsIn(type: VehicleType): boolean {
  return type === 'phev' || type === 'bev'
}

/** Anything but a plain hybrid, whose battery is a buffer rather than something to plan with. */
export function mayPlugIn(type: VehicleType): boolean {
  return type !== 'hev'
}

/** Burns fuel: a hybrid, plug-in or not. */
export function burnsFuel(type: VehicleType): boolean {
  return type === 'hev' || type === 'phev'
}

/** Anything but a BEV. */
export function mayBurnFuel(type: VehicleType): boolean {
  return type !== 'bev'
}
