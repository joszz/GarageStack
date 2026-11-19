import type { Vehicle } from '@/services/vehicleApi'

export interface VehicleLabel {
  id: number
  vin: string
  label: string
}

function baseName(v: Vehicle): string {
  return v.model ?? v.series ?? v.vin
}

/**
 * What each car is called wherever the app has to tell cars apart: the car picker and the
 * notifications. Its model, and when two cars share a model, the end of the VIN as well. The
 * Worker names cars the same way in push messages (PushSenderService.TitleWithCar).
 */
export function vehicleLabels(vehicles: readonly Vehicle[]): VehicleLabel[] {
  const counts = new Map<string, number>()
  for (const v of vehicles) counts.set(baseName(v), (counts.get(baseName(v)) ?? 0) + 1)
  return vehicles.map((v) => {
    const name = baseName(v)
    const shared = (counts.get(name) ?? 0) > 1 && name !== v.vin
    return { id: v.id, vin: v.vin, label: shared ? `${name} (…${v.vin.slice(-6)})` : name }
  })
}

/** A notification's title with the car in front, as "MG4 · Engine started"; unchanged without one. */
export function titleWithCar(title: string, carLabel: string | null): string {
  return carLabel ? `${carLabel} · ${title}` : title
}
