import type { ChargeSession, VehicleType } from '@/services/vehicleApi'
import { trustedBatteryKwh, whPerKm } from './energy'

/** The periods the charge history offers, in days. The API reads at most a year at a time. */
export const CHARGE_HISTORY_DAYS = [30, 90, 365] as const
export type ChargeHistoryDays = (typeof CHARGE_HISTORY_DAYS)[number]
export const DEFAULT_CHARGE_HISTORY_DAYS: ChargeHistoryDays = 90

export type ChargingKind = 'AC' | 'DC'

/**
 * Whether a charge is AC or DC, from drivetrain/chargingType, as Core's ChargingKind reads it. The
 * gateway passes on the SAIC API's raw number there, which the API does not document. The gateway's
 * README lists 6 as AC and 7 as DC, both checked against logged responses. Other numbers describe
 * the car's state, such as 10 for a charge that has ended, so they give null. The demo and the
 * charge history already write "AC" and "DC".
 */
export function chargingKind(value: string | null): ChargingKind | null {
  switch (value?.trim().toUpperCase()) {
    case '6':
    case 'AC':
      return 'AC'
    case '7':
    case 'DC':
      return 'DC'
    default:
      return null
  }
}

/** What the charge history needs to know about the car to trust a battery figure. */
export interface ChargeEnergyContext {
  vehicleType: VehicleType
  configuredCapacityKwh: number | null
}

/** Since the charge before: the distance driven, the battery energy it took, and the two as a rate. */
export interface SincePreviousCharge {
  distanceKm: number | null
  energyKwh: number | null
  /** Electric consumption in Wh/km, only on a BEV: a PHEV's distance includes petrol kilometres. */
  whPerKm: number | null
}

function startKwh(session: ChargeSession, context: ChargeEnergyContext): number | null {
  return trustedBatteryKwh(
    session.startSocPercent,
    session.startBatteryKwh,
    context.vehicleType,
    context.configuredCapacityKwh,
  )
}

function endKwh(session: ChargeSession, context: ChargeEnergyContext): number | null {
  return trustedBatteryKwh(
    session.endSocPercent,
    session.endBatteryKwh,
    context.vehicleType,
    context.configuredCapacityKwh,
  )
}

function difference(later: number | null, earlier: number | null): number | null {
  if (later === null || earlier === null) return null
  const value = later - earlier
  return Number.isFinite(value) && value >= 0 ? value : null
}

/** How much the battery took in this session, in kWh, when the car's figures can be trusted. */
export function energyGainedKwh(
  session: ChargeSession,
  context: ChargeEnergyContext,
): number | null {
  return difference(endKwh(session, context), startKwh(session, context))
}

/** Since the charge before this one: `previous` is the next older session, or null. */
export function sincePreviousCharge(
  session: ChargeSession,
  previous: ChargeSession | null,
  context: ChargeEnergyContext,
): SincePreviousCharge {
  if (!previous) return { distanceKm: null, energyKwh: null, whPerKm: null }

  const distanceKm = difference(session.odometerKm, previous.odometerKm)
  const energyKwh = difference(endKwh(previous, context), startKwh(session, context))
  return {
    distanceKm,
    energyKwh,
    whPerKm: context.vehicleType === 'bev' ? whPerKm(energyKwh, distanceKm) : null,
  }
}
