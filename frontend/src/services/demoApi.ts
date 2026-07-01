import { send } from '@/services/apiCore'

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
  chargerConnected?: boolean | null
  isCharging?: boolean | null
  lightsMainBeam?: boolean | null
  lightsDippedBeam?: boolean | null
  lightsSide?: boolean | null
  evSocPercent?: number | null
  interiorTemperature?: number | null
  exteriorTemperature?: number | null
}

export const demoApi = {
  setStatus: (vin: string, override: DemoStatusOverride) =>
    send(`/api/demo/status/${vin}`, 'POST', override),
}
