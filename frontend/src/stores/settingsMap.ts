import { defineStore } from 'pinia'
import { reactive, toRefs } from 'vue'
import { persistSettings, readLegacyBlob } from './settingsShared'
import { isFuelType } from '@/utils/fuelTypes'

const STORAGE_KEY = 'garagestack-settings-map'

interface MapSettings {
  routeOutlineEnabled: boolean
  snapToRoadsEnabled: boolean
  heatmapEnabled: boolean
  speedOverlayEnabled: boolean
  speedLimitOverlayEnabled: boolean
  chargingStationsEnabled: boolean
  chargingMinPowerKw: number
  chargingMaxPowerKw: number
  fuelStationsEnabled: boolean
  fuelBrandFilter: string[]
  fuelTypeFilter: string[]
  serviceAreasEnabled: boolean
  speedCamerasEnabled: boolean
}

// A function rather than a constant: the lists in it are the store's own once loaded, and a
// shared array would let one store's edit show up in every other store built from the defaults.
function defaultsFor(): MapSettings {
  return {
    routeOutlineEnabled: false,
    snapToRoadsEnabled: true,
    heatmapEnabled: true,
    speedOverlayEnabled: false,
    speedLimitOverlayEnabled: false,
    chargingStationsEnabled: false,
    chargingMinPowerKw: 0,
    chargingMaxPowerKw: 0,
    fuelStationsEnabled: false,
    fuelBrandFilter: [],
    fuelTypeFilter: [],
    serviceAreasEnabled: false,
    speedCamerasEnabled: false,
  }
}

function parseMapFields(parsed: Record<string, unknown>): MapSettings {
  return {
    routeOutlineEnabled: parsed.routeOutlineEnabled === true,
    snapToRoadsEnabled: parsed.snapToRoadsEnabled !== false,
    heatmapEnabled: parsed.heatmapEnabled !== false,
    speedOverlayEnabled: parsed.speedOverlayEnabled === true,
    speedLimitOverlayEnabled: parsed.speedLimitOverlayEnabled === true,
    chargingStationsEnabled: parsed.chargingStationsEnabled === true,
    chargingMinPowerKw:
      typeof parsed.chargingMinPowerKw === 'number' ? parsed.chargingMinPowerKw : 0,
    chargingMaxPowerKw:
      typeof parsed.chargingMaxPowerKw === 'number' ? parsed.chargingMaxPowerKw : 0,
    fuelStationsEnabled: parsed.fuelStationsEnabled === true,
    fuelBrandFilter: Array.isArray(parsed.fuelBrandFilter)
      ? (parsed.fuelBrandFilter as string[])
      : [],
    // Unlike brands, which are whatever OSM holds, fuel types are a fixed set. A stale or
    // hand-edited id would match no station and silently empty the layer, so drop it here.
    fuelTypeFilter: Array.isArray(parsed.fuelTypeFilter)
      ? (parsed.fuelTypeFilter as unknown[]).filter(
          (value): value is string => typeof value === 'string' && isFuelType(value),
        )
      : [],
    serviceAreasEnabled: parsed.serviceAreasEnabled === true,
    speedCamerasEnabled: parsed.speedCamerasEnabled === true,
  }
}

function loadMapSettings(): MapSettings {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (raw) return parseMapFields(JSON.parse(raw))
  } catch {
    // ignore parse errors
  }
  const legacy = readLegacyBlob()
  if (legacy) return parseMapFields(legacy)
  return defaultsFor()
}

export const useMapSettingsStore = defineStore('settingsMap', () => {
  const settings = reactive(loadMapSettings())
  persistSettings({
    storageKey: STORAGE_KEY,
    section: 'map',
    state: settings,
    parse: parseMapFields,
  })
  return toRefs(settings)
})
