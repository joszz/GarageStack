import { TIME_ZONE_REGIONS } from '@/utils/timeZoneRegions'

/** An ISO 3166 region code, such as "NL". */
export type Region = string

/** How a region reads the clock: 0 to 23, or 1 to 12 with AM and PM. */
export type HourCycle = 'h23' | 'h12'

/** Every region with a time zone of its own, which is every region a browser can be in. */
export const REGIONS: readonly Region[] = Object.keys(TIME_ZONE_REGIONS)

const KNOWN_REGIONS = new Set(REGIONS)

export function isRegion(value: unknown): value is Region {
  return typeof value === 'string' && KNOWN_REGIONS.has(value)
}

/** What a browser tells about where it is: its time zone, and its languages, most preferred first. */
export interface ClientSignals {
  timeZone: string | undefined
  languages: readonly string[]
}

export function clientSignals(): ClientSignals {
  let timeZone: string | undefined
  try {
    timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone
  } catch {
    timeZone = undefined
  }
  const languages = navigator.languages?.length ? navigator.languages : [navigator.language]
  return { timeZone, languages }
}

let regionOfZone: Map<string, Region> | null = null

function regionOfTimeZone(zone: string): Region | null {
  regionOfZone ??= new Map(
    Object.entries(TIME_ZONE_REGIONS).flatMap(([region, zones]) =>
      zones.split(' ').map((z): [string, Region] => [z, region]),
    ),
  )
  return regionOfZone.get(zone) ?? null
}

function parsedLocale(tag: string): Intl.Locale | null {
  try {
    return new Intl.Locale(tag)
  } catch {
    return null
  }
}

/**
 * The region whose conventions this browser most likely lives by. The time zone comes first: the
 * system sets it from where the device is, while the language is often left at a default such as
 * en-US that says nothing about where its user drives. A region named in one of the languages
 * comes next, then the region the first language is most spoken in.
 */
export function detectRegion(signals: ClientSignals = clientSignals()): Region {
  const fromZone = signals.timeZone ? regionOfTimeZone(signals.timeZone) : null
  if (fromZone) return fromZone
  for (const tag of signals.languages) {
    const region = parsedLocale(tag)?.region
    if (isRegion(region)) return region
  }
  const likely = parsedLocale(signals.languages[0] ?? 'en')?.maximize().region
  return isRegion(likely) ? likely : 'US'
}

/**
 * The clock a region reads, from the conventions of the language most spoken there: a 24-hour
 * clock in Britain and the Netherlands, 12 hours in the United States and Australia.
 */
export function regionHourCycle(region: Region): HourCycle {
  // maximize() adds a script ("en-Latn-GB"), which no set of conventions is filed under.
  const language = parsedLocale(`und-${region}`)?.maximize().language ?? 'en'
  const { hour12 } = new Intl.DateTimeFormat(`${language}-${region}`, {
    hour: 'numeric',
  }).resolvedOptions()
  return hour12 ? 'h12' : 'h23'
}

const displayNames = new Map<string, Intl.DisplayNames>()

/** The region's name in the given language: "Netherlands", "Nederland". */
export function regionName(region: Region, language: string): string {
  try {
    let names = displayNames.get(language)
    if (!names) {
      names = new Intl.DisplayNames([language], { type: 'region' })
      displayNames.set(language, names)
    }
    return names.of(region) ?? region
  } catch {
    return region
  }
}
