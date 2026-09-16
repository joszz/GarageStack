import type { TelemetrySnapshot, Trip } from '@/services/vehicleApi'
import type { VehicleType } from '@/stores/vehicle'
import { whPerKm } from '@/utils/energy'

/**
 * Everything the dashboard needs to know about a card, in one place: its icon, whether it is
 * visible by default for a given vehicle type, and whether the current telemetry carries
 * anything for it. The card's markup lives in DashboardCardContent; everything else about it
 * lives here, so adding a card means adding one entry rather than touching four lists.
 */
export interface CardDataContext {
  status: TelemetrySnapshot
  vehicleType: VehicleType
  latestTrip: Trip | null
}

interface CardDefinition {
  id: string
  /** FontAwesome icon, used for the skeleton, the edit-mode placeholder and plain status cards. */
  icon: string
  /** Visible on a fresh layout for this vehicle type. Defaults to visible for every type. */
  defaultVisible?: (type: VehicleType) => boolean
  /** Whether the telemetry holds a value worth showing. Defaults to "always something to show". */
  hasData?: (ctx: CardDataContext) => boolean
}

const canChargeExternally = (type: VehicleType) => type === 'phev' || type === 'bev'

export const CARD_DEFINITIONS = [
  {
    id: 'fuelLevel',
    icon: 'gas-pump',
    defaultVisible: (type) => type !== 'bev',
    hasData: ({ status }) => status.fuelLevelPercent !== null,
  },
  {
    id: 'fuelRange',
    icon: 'road',
    defaultVisible: (type) => type !== 'bev',
    hasData: ({ status }) => status.fuelRangeKm !== null,
  },
  {
    id: 'evBattery',
    icon: 'bolt',
    defaultVisible: (type) => type !== 'hev',
    hasData: ({ status }) => status.evSocPercent !== null,
  },
  {
    id: 'charging',
    icon: 'plug',
    defaultVisible: canChargeExternally,
    hasData: ({ status }) => status.isCharging !== null,
  },
  { id: 'odometer', icon: 'gauge' },
  { id: 'battery12v', icon: 'battery-three-quarters' },
  { id: 'doors', icon: 'lock' },
  { id: 'windows', icon: 'car-side' },
  {
    id: 'sunRoof',
    icon: 'sun',
    defaultVisible: () => false,
    hasData: ({ status }) => status.sunRoofOpen !== null,
  },
  { id: 'climate', icon: 'wind' },
  { id: 'hvBattery', icon: 'battery-half' },
  { id: 'findMyCar', icon: 'car-burst' },
  { id: 'lights', icon: 'lightbulb' },
  {
    id: 'efficiencyDistance',
    icon: 'route',
    hasData: ({ status }) => status.mileageOfTheDay !== null,
  },
  {
    id: 'efficiencyEnergy',
    icon: 'plug-circle-bolt',
    hasData: ({ status }) => status.powerUsageOfDay !== null,
  },
  {
    id: 'efficiencyCharge',
    icon: 'battery-full',
    defaultVisible: (type) => type !== 'hev',
    hasData: ({ status, vehicleType }) =>
      status.mileageSinceLastCharge !== null && vehicleType !== 'hev' && vehicleType !== 'unknown',
  },
  {
    id: 'efficiencyRatio',
    icon: 'leaf',
    // Two presentations share this card: Wh/km while driving data is available, and a fuel
    // economy estimate for vehicles that burn fuel. DashboardCardContent picks between them.
    hasData: (ctx) => hasEnergyEfficiency(ctx) || hasFuelEconomy(ctx),
  },
  {
    id: 'speed',
    icon: 'gauge-high',
    defaultVisible: () => false,
    hasData: ({ status }) => status.speed !== null,
  },
  { id: 'activeTrip', icon: 'location-arrow' },
  {
    id: 'remainingCharge',
    icon: 'clock',
    defaultVisible: canChargeExternally,
    hasData: ({ status, vehicleType }) =>
      status.remainingChargingTime !== null && canChargeExternally(vehicleType),
  },
  {
    id: 'chargingSession',
    icon: 'plug-circle-bolt',
    defaultVisible: canChargeExternally,
    hasData: ({ vehicleType }) => canChargeExternally(vehicleType),
  },
  {
    id: 'batteryHeating',
    icon: 'temperature-arrow-up',
    defaultVisible: canChargeExternally,
    hasData: ({ vehicleType }) => canChargeExternally(vehicleType),
  },
  {
    id: 'topSpeed',
    icon: 'gauge-high',
    hasData: ({ latestTrip }) =>
      latestTrip !== null && latestTrip.points.some((p) => p.speed !== null),
  },
  { id: 'maintenance', icon: 'screwdriver-wrench' },
] as const satisfies readonly CardDefinition[]

export type CardId = (typeof CARD_DEFINITIONS)[number]['id']

export interface CardConfig {
  id: CardId
  visible: boolean
}

// The same list, widened: `as const` keeps each entry's exact shape, which hides the optional
// members from entries that leave them out. Every lookup below reads it through this view.
const DEFINITIONS: readonly (Omit<CardDefinition, 'id'> & { id: CardId })[] = CARD_DEFINITIONS

const BY_ID = new Map(DEFINITIONS.map((card) => [card.id, card]))

/** Every card id that exists, in registry order. */
export const ALL_CARD_IDS: CardId[] = DEFINITIONS.map((card) => card.id)

export function cardIcon(id: CardId): string {
  return BY_ID.get(id)!.icon
}

/**
 * Whether the vehicle reports anything for this card. Cards without data are pushed to the end
 * of a fresh layout and skipped while rendering, so an empty slot never reaches the grid.
 */
export function cardHasData(id: CardId, ctx: CardDataContext): boolean {
  return BY_ID.get(id)?.hasData?.(ctx) ?? true
}

/** The default layout for a vehicle type: visible cards first, in registry order. */
export function defaultCards(type: VehicleType = 'unknown'): CardConfig[] {
  const all = DEFINITIONS.map((card) => ({
    id: card.id,
    visible: card.defaultVisible?.(type) ?? true,
  }))
  return [...all.filter((c) => c.visible), ...all.filter((c) => !c.visible)]
}

/** Wh/km over today's driving, the efficiencyRatio card's primary presentation. */
export function hasEnergyEfficiency({ status }: CardDataContext): boolean {
  return whPerKm(status.powerUsageOfDay, status.mileageOfTheDay) !== null
}

/** Fuel economy from the range computer, the efficiencyRatio card's fallback for fuel burners. */
export function hasFuelEconomy({ status, vehicleType }: CardDataContext): boolean {
  return (
    (vehicleType === 'hev' || vehicleType === 'phev') &&
    status.fuelRangeKm !== null &&
    status.fuelLevelPercent !== null &&
    status.fuelLevelPercent > 0
  )
}
