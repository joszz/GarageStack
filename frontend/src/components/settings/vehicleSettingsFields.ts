import type { VehicleSettingsValues, VehicleType } from '@/services/vehicleApi'

export type NumberField = Exclude<keyof VehicleSettingsValues, 'vehicleType'>

export interface NumberFieldDefinition {
  key: NumberField
  /** Shown in the browser's pressure unit and sent in bar; the others are shown as the API takes them. */
  quantity?: 'pressure'
  /** The API's range, in its own unit (see VehicleSettingsRules on the server). */
  min: number
  max: number
  /** Decimals of the default shown as the placeholder. */
  decimals: number
  /** Whether the field is offered for a car of this type. */
  appliesTo: (type: VehicleType) => boolean
}

const always = () => true
const burnsFuel = (type: VehicleType) => type !== 'bev'
const plugsIn = (type: VehicleType) => type !== 'hev'

/** The per-car number settings, in the order the settings page lists them. */
export const numberFields: readonly NumberFieldDefinition[] = [
  // A plain hybrid gets none: what it calls a battery is a small buffer the kWh figures ignore.
  { key: 'hvBatteryCapacityKwh', min: 0.5, max: 200, decimals: 2, appliesTo: plugsIn },
  { key: 'tyreLowBar', quantity: 'pressure', min: 1, max: 5, decimals: 2, appliesTo: always },
  { key: 'tyreGoodBar', quantity: 'pressure', min: 1, max: 5, decimals: 2, appliesTo: always },
  { key: 'tyreHighBar', quantity: 'pressure', min: 1, max: 5, decimals: 2, appliesTo: always },
  { key: 'tyreDropBar', quantity: 'pressure', min: 0, max: 1, decimals: 2, appliesTo: always },
  { key: 'auxBatteryLowVolt', min: 0, max: 15, decimals: 1, appliesTo: always },
  { key: 'lowFuelPercent', min: 0, max: 100, decimals: 0, appliesTo: burnsFuel },
  { key: 'lowEvPercent', min: 0, max: 100, decimals: 0, appliesTo: plugsIn },
]
