import type { TelemetrySnapshot, Vehicle } from '@/services/vehicleApi'

type LocationTelemetry = Pick<
  TelemetrySnapshot,
  | 'vehicleId'
  | 'latitude'
  | 'longitude'
  | 'speed'
  | 'engineRunning'
  | 'currentJourneyDistance'
  | 'lastVehicleStateAt'
>

export interface CarLocation {
  vehicleId: number
  latitude: number
  longitude: number
  mayBeMoving: boolean
  vehicleUpdatedAt: string | null
}

export function carLocation(
  vehicle: Pick<Vehicle, 'id'> | null,
  status: LocationTelemetry | null,
): CarLocation | null {
  if (!vehicle || !status || status.vehicleId !== vehicle.id) return null

  const { latitude, longitude } = status
  if (
    typeof latitude !== 'number' ||
    typeof longitude !== 'number' ||
    !Number.isFinite(latitude) ||
    !Number.isFinite(longitude) ||
    latitude < -90 ||
    latitude > 90 ||
    longitude < -180 ||
    longitude > 180
  )
    return null

  const onTrip =
    status.currentJourneyDistance != null &&
    Number.isFinite(status.currentJourneyDistance) &&
    status.currentJourneyDistance > 0
  // Merged telemetry can retain an old speed after the engine has reported that it is off.
  const reportsMotion =
    status.engineRunning !== false &&
    status.speed != null &&
    Number.isFinite(status.speed) &&
    status.speed > 0
  const updatedAt = status.lastVehicleStateAt

  return {
    vehicleId: vehicle.id,
    latitude,
    longitude,
    mayBeMoving: onTrip || reportsMotion,
    vehicleUpdatedAt: updatedAt && !Number.isNaN(Date.parse(updatedAt)) ? updatedAt : null,
  }
}
