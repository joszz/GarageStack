// OSM describes what a station sells with one `fuel:*` tag per product, at a granularity no
// driver thinks in: `fuel:octane_95`, `fuel:octane_98`, `fuel:e10` and `fuel:e5` are all simply
// "petrol" at the pump. This maps those keys onto the handful of types worth filtering on, the
// way canonicalFuelBrand does for chains.

/** The types the filter offers, in the order the dropdown lists them. Ids are i18n keys too. */
export const FUEL_TYPES = [
  'petrol',
  'diesel',
  'lpg',
  'cng',
  'lng',
  'hydrogen',
  'e85',
  'adblue',
] as const

export type FuelType = (typeof FUEL_TYPES)[number]

const TAG_PREFIX = 'fuel:'

// Key as OSM writes it minus the prefix, lowercased, mapped onto the type a driver would pick.
// E85 stays apart from petrol because a car that takes petrol generally cannot run on it, and
// `fuel:electricity` is deliberately absent: charging has its own layer, and leaving it unmapped
// keeps an electricity-only site out of the results for a petrol or diesel filter.
const TAG_TO_TYPE: Record<string, FuelType> = {
  octane_91: 'petrol',
  octane_92: 'petrol',
  octane_95: 'petrol',
  octane_98: 'petrol',
  octane_100: 'petrol',
  e5: 'petrol',
  e10: 'petrol',
  diesel: 'diesel',
  gtl_diesel: 'diesel',
  hgv_diesel: 'diesel',
  biodiesel: 'diesel',
  'diesel:class2': 'diesel',
  lpg: 'lpg',
  cng: 'cng',
  lng: 'lng',
  lh2: 'hydrogen',
  h2: 'hydrogen',
  e85: 'e85',
  adblue: 'adblue',
}

// A tag says the product is sold unless it explicitly says otherwise. OSM's own value is "no",
// but hand-edited data also carries "false" and "0" for the same thing.
const NOT_SOLD = new Set(['no', 'false', '0'])

interface FuelTagScan {
  /** Types the station advertises. */
  available: Set<FuelType>
  /**
   * The station says something about its range, whatever we made of it. A station with no
   * `fuel:*` tag at all is undescribed rather than empty, which is the common case in OSM and
   * the reason the filter keeps it.
   */
  describesRange: boolean
}

function scanFuelTags(tags: Record<string, string> | null | undefined): FuelTagScan {
  const available = new Set<FuelType>()
  let describesRange = false
  if (!tags) return { available, describesRange }

  for (const [key, value] of Object.entries(tags)) {
    const lowerKey = key.toLowerCase()
    if (!lowerKey.startsWith(TAG_PREFIX)) continue
    describesRange = true

    const type = TAG_TO_TYPE[lowerKey.slice(TAG_PREFIX.length)]
    if (type && !NOT_SOLD.has(value.trim().toLowerCase())) available.add(type)
  }

  return { available, describesRange }
}

/** The types a station advertises, in the order {@link FUEL_TYPES} lists them. */
export function stationFuelTypes(tags: Record<string, string> | null | undefined): FuelType[] {
  const { available } = scanFuelTags(tags)
  return FUEL_TYPES.filter((type) => available.has(type))
}

/**
 * Whether a station survives the filter. An empty selection keeps everything, and so does a
 * station that lists no fuels at all: most of OSM's stations are tagged that way, and hiding
 * them would empty the map rather than narrow it. Only a station that describes its range and
 * does not sell any of the selected types is dropped.
 */
export function matchesFuelTypeFilter(
  tags: Record<string, string> | null | undefined,
  selected: readonly string[],
): boolean {
  if (selected.length === 0) return true

  const { available, describesRange } = scanFuelTags(tags)
  if (!describesRange) return true

  return selected.some((type) => available.has(type as FuelType))
}

/** Guards settings restored from storage, so a stale id cannot silently hide every station. */
export function isFuelType(value: string): value is FuelType {
  return (FUEL_TYPES as readonly string[]).includes(value)
}
