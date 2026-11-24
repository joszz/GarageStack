import { send } from '@/services/apiCore'

/**
 * What the Demo Controls change on one demo car; a field left out keeps its value. Metric, like
 * the telemetry it stands in for: °C, km/h, bar, volts and kW.
 */
export interface DemoStatusOverride {
  isLocked?: boolean | null
  engineRunning?: boolean | null
  climateOn?: boolean | null
  driverDoorOpen?: boolean | null
  passengerDoorOpen?: boolean | null
  rearLeftDoorOpen?: boolean | null
  rearRightDoorOpen?: boolean | null
  trunkOpen?: boolean | null
  bonnetOpen?: boolean | null
  driverWindowOpen?: boolean | null
  passengerWindowOpen?: boolean | null
  rearLeftWindowOpen?: boolean | null
  rearRightWindowOpen?: boolean | null
  /** Pulling the cable ends a charge. */
  chargerConnected?: boolean | null
  /** Starting a charge plugs the cable in. */
  isCharging?: boolean | null
  lightsMainBeam?: boolean | null
  lightsDippedBeam?: boolean | null
  lightsSide?: boolean | null
  evSocPercent?: number | null
  interiorTemperature?: number | null
  exteriorTemperature?: number | null
  speed?: number | null
  isAvailable?: boolean | null
  fuelLevelPercent?: number | null
  tyrePressureFrontLeft?: number | null
  tyrePressureFrontRight?: number | null
  tyrePressureRearLeft?: number | null
  tyrePressureRearRight?: number | null
  batteryVoltage?: number | null
  chargingType?: 'AC' | 'DC' | null
  chargingPowerKw?: number | null
  /** True puts the car on the road (engine on, unplugged, moving), false parks it. */
  driving?: boolean | null
}

export const demoApi = {
  setStatus: (vin: string, override: DemoStatusOverride) =>
    send(`/api/demo/status/${vin}`, 'POST', override),
  /** Plays the gateway signing in to the MG account, or failing to. */
  setGateway: (signedIn: boolean) => send('/api/demo/gateway', 'POST', { signedIn }),
}
