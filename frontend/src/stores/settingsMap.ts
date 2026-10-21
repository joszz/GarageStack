import { defineStore } from 'pinia'
import { reactive, toRefs } from 'vue'
import { loadStoredSettings, persistSettings } from './settingsShared'
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
  /**
   * Whether the speed key on the map is open; null until it is opened or folded here, when it
   * follows the screen. Kept per device, as a phone's map is too short for what suits a desktop.
   */
  legendExpanded: boolean | null
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
    legendExpanded: null,
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
    legendExpanded: typeof parsed.legendExpanded === 'boolean' ? parsed.legendExpanded : null,
  }
}

export const useMapSettingsStore = defineStore('settingsMap', () => {
  const settings = reactive(loadStoredSettings(STORAGE_KEY, parseMapFields, defaultsFor))
  persistSettings({
    storageKey: STORAGE_KEY,
    section: 'map',
    state: settings,
    parse: parseMapFields,
    localKeys: ['legendExpanded'],
  })
  return toRefs(settings)
})
