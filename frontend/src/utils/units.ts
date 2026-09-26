import { formatNumber } from '@/utils/format'

// The API speaks metric and nothing else: kilometres, bar, degrees Celsius, litres. The interface
// converts at the last moment, when a value is shown or typed, so a preference never reaches the
// database and switching it back and forth loses nothing. This file holds the conversions and the
// unit each preference implies; composables/useUnits binds them to the settings and the language.

export type DistanceUnit = 'km' | 'mi'
export type TemperatureUnit = 'celsius' | 'fahrenheit'
export type PressureUnit = 'bar' | 'psi' | 'kpa'
export type FuelConsumptionUnit = 'l100km' | 'mpgUk' | 'mpgUs'

export interface UnitPreferences {
  distance: DistanceUnit
  temperature: TemperatureUnit
  pressure: PressureUnit
  fuelConsumption: FuelConsumptionUnit
}

export const DISTANCE_UNITS: readonly DistanceUnit[] = ['km', 'mi']
export const TEMPERATURE_UNITS: readonly TemperatureUnit[] = ['celsius', 'fahrenheit']
export const PRESSURE_UNITS: readonly PressureUnit[] = ['bar', 'psi', 'kpa']
export const FUEL_CONSUMPTION_UNITS: readonly FuelConsumptionUnit[] = ['l100km', 'mpgUk', 'mpgUs']

export const METRIC_UNITS: Readonly<UnitPreferences> = {
  distance: 'km',
  temperature: 'celsius',
  pressure: 'bar',
  fuelConsumption: 'l100km',
}

/**
 * Something the API measures, each always in one metric unit: kilometres, km/h, Wh/km, km per
 * percent of the tank, bar, degrees Celsius, L/100 km and litres respectively.
 */
export type Quantity =
  | 'distance'
  | 'speed'
  | 'energyPerDistance'
  | 'distancePerPercent'
  | 'pressure'
  | 'temperature'
  | 'fuelConsumption'
  | 'volume'

interface UnitDefinition {
  /** i18n key of the unit's symbol. */
  symbol: string
  fromMetric: (value: number) => number
  /** The way back, for a value typed in this unit. */
  toMetric: (value: number) => number
  /** Decimals when a caller has no reason to choose its own: enough to see a change, no more. */
  decimals: number
  /**
   * True when a bigger number means less of the quantity: miles per gallon counts the other way
   * round from litres per 100 km, so "lower is better" turns into "higher is better".
   */
  reciprocal: boolean
}

// Exact by definition, apart from psi, which is derived from the pound and the inch.
const KM_PER_MILE = 1.609344
const PSI_PER_BAR = 14.503_773_8
const KPA_PER_BAR = 100
const LITRES_PER_UK_GALLON = 4.546_09
const LITRES_PER_US_GALLON = 3.785_411_784

// L/100 km and mpg are each other's reciprocal: x L/100 km is 100 km per x litres, which in miles
// per gallon is this constant over x, and the same constant takes mpg back to L/100 km.
const MPG_UK_BY_L_PER_100KM = (100 * LITRES_PER_UK_GALLON) / KM_PER_MILE
const MPG_US_BY_L_PER_100KM = (100 * LITRES_PER_US_GALLON) / KM_PER_MILE

function scaled(symbol: string, factor: number, decimals: number): UnitDefinition {
  return {
    symbol,
    fromMetric: (value) => value * factor,
    toMetric: (value) => value / factor,
    decimals,
    reciprocal: false,
  }
}

function reciprocalOf(symbol: string, constant: number, decimals: number): UnitDefinition {
  return {
    symbol,
    fromMetric: (value) => constant / value,
    toMetric: (value) => constant / value,
    decimals,
    reciprocal: true,
  }
}

const DISTANCE: Record<DistanceUnit, UnitDefinition> = {
  km: scaled('units.km', 1, 1),
  mi: scaled('units.mi', 1 / KM_PER_MILE, 1),
}

const SPEED: Record<DistanceUnit, UnitDefinition> = {
  km: scaled('units.kmh', 1, 0),
  mi: scaled('units.mph', 1 / KM_PER_MILE, 0),
}

// Energy per distance grows with the distance unit: a mile takes 1.6 times the energy of a km.
const ENERGY_PER_DISTANCE: Record<DistanceUnit, UnitDefinition> = {
  km: scaled('units.whPerKm', 1, 0),
  mi: scaled('units.whPerMi', KM_PER_MILE, 0),
}

const DISTANCE_PER_PERCENT: Record<DistanceUnit, UnitDefinition> = {
  km: scaled('units.kmPerPercent', 1, 1),
  mi: scaled('units.miPerPercent', 1 / KM_PER_MILE, 1),
}

// A tyre gauge reads whole psi and whole kPa; bar needs two decimals to show the same step.
const PRESSURE: Record<PressureUnit, UnitDefinition> = {
  bar: scaled('units.bar', 1, 2),
  psi: scaled('units.psi', PSI_PER_BAR, 0),
  kpa: scaled('units.kpa', KPA_PER_BAR, 0),
}

const TEMPERATURE: Record<TemperatureUnit, UnitDefinition> = {
  celsius: scaled('units.celsius', 1, 1),
  fahrenheit: {
    symbol: 'units.fahrenheit',
    fromMetric: (celsius) => (celsius * 9) / 5 + 32,
    toMetric: (fahrenheit) => ((fahrenheit - 32) * 5) / 9,
    decimals: 1,
    reciprocal: false,
  },
}

const FUEL_CONSUMPTION: Record<FuelConsumptionUnit, UnitDefinition> = {
  l100km: scaled('units.l100km', 1, 1),
  mpgUk: reciprocalOf('units.mpg', MPG_UK_BY_L_PER_100KM, 1),
  mpgUs: reciprocalOf('units.mpg', MPG_US_BY_L_PER_100KM, 1),
}

// Fuel is counted in the gallon the consumption is quoted in, so the two always agree.
const VOLUME: Record<FuelConsumptionUnit, UnitDefinition> = {
  l100km: scaled('units.litre', 1, 1),
  mpgUk: scaled('units.gallon', 1 / LITRES_PER_UK_GALLON, 2),
  mpgUs: scaled('units.gallon', 1 / LITRES_PER_US_GALLON, 2),
}

const UNIT_OF: { [Q in Quantity]: (preferences: UnitPreferences) => UnitDefinition } = {
  distance: (p) => DISTANCE[p.distance],
  speed: (p) => SPEED[p.distance],
  energyPerDistance: (p) => ENERGY_PER_DISTANCE[p.distance],
  distancePerPercent: (p) => DISTANCE_PER_PERCENT[p.distance],
  pressure: (p) => PRESSURE[p.pressure],
  temperature: (p) => TEMPERATURE[p.temperature],
  fuelConsumption: (p) => FUEL_CONSUMPTION[p.fuelConsumption],
  volume: (p) => VOLUME[p.fuelConsumption],
}

/** A value ready to show: the rounded number and its unit, kept apart for cards that style them apart. */
export interface Measure {
  value: string
  unit: string
}

export interface MeasureOptions {
  /** Overrides the unit's own number of decimals. */
  decimals?: number
  /** Thousands separators, for a large whole number such as an odometer reading. */
  grouped?: boolean
}

/** How an odometer reading or a service interval reads: whole units, grouped by thousands. */
export const ODOMETER_FORMAT: Readonly<MeasureOptions> = { decimals: 0, grouped: true }

// `t` is injected rather than obtained via useI18n() so pure functions (trip rows, the CSV export)
// can format with it and tests can construct one directly. A structural type avoids coupling to
// a locale's message-key generics.
type Translate = (key: string) => string

/**
 * Shows metric values in the units a browser prefers, and turns what is typed back into metric.
 * Every displayed distance, speed, pressure, temperature and fuel figure goes through one of these,
 * so a unit is chosen in exactly one place.
 */
export class UnitFormatter {
  constructor(
    readonly preferences: Readonly<UnitPreferences>,
    private readonly t: Translate,
  ) {}

  private unitOf(quantity: Quantity): UnitDefinition {
    return UNIT_OF[quantity](this.preferences)
  }

  /** A metric value in the preferred unit, unrounded: for charts and form fields. */
  convert(quantity: Quantity, metric: number): number {
    return this.unitOf(quantity).fromMetric(metric)
  }

  /** A value in the preferred unit back in the metric unit the API takes. */
  toMetric(quantity: Quantity, value: number): number {
    return this.unitOf(quantity).toMetric(value)
  }

  symbol(quantity: Quantity): string {
    return this.t(this.unitOf(quantity).symbol)
  }

  decimals(quantity: Quantity): number {
    return this.unitOf(quantity).decimals
  }

  /** Whether a bigger number means less of the quantity, as with mpg. */
  isReciprocal(quantity: Quantity): boolean {
    return this.unitOf(quantity).reciprocal
  }

  /** Null when there is nothing to show, including a value no unit can express (0 L/100 km in mpg). */
  measure(quantity: Quantity, metric: number | null, options: MeasureOptions = {}): Measure | null {
    if (metric === null) return null
    const converted = this.convert(quantity, metric)
    if (!Number.isFinite(converted)) return null
    const decimals = options.decimals ?? this.decimals(quantity)
    return {
      value: formatNumber(converted, decimals, options.grouped),
      unit: this.symbol(quantity),
    }
  }

  /** "12.3 km", or null when there is nothing to show. */
  format(quantity: Quantity, metric: number | null, options: MeasureOptions = {}): string | null {
    const m = this.measure(quantity, metric, options)
    return m && `${m.value} ${m.unit}`
  }
}
