import { defineStore } from 'pinia'
import { ref, watch } from 'vue'
import { readLegacyBlob, createDebouncedSave } from './settingsShared'
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
}

const defaults: MapSettings = {
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
  return { ...defaults }
}

export const useMapSettingsStore = defineStore('settingsMap', () => {
  const loaded = loadMapSettings()
  const routeOutlineEnabled = ref<boolean>(loaded.routeOutlineEnabled)
  const snapToRoadsEnabled = ref<boolean>(loaded.snapToRoadsEnabled)
  const heatmapEnabled = ref<boolean>(loaded.heatmapEnabled)
  const speedOverlayEnabled = ref<boolean>(loaded.speedOverlayEnabled)
  const speedLimitOverlayEnabled = ref<boolean>(loaded.speedLimitOverlayEnabled)
  const chargingStationsEnabled = ref<boolean>(loaded.chargingStationsEnabled)
  const chargingMinPowerKw = ref<number>(loaded.chargingMinPowerKw)
  const chargingMaxPowerKw = ref<number>(loaded.chargingMaxPowerKw)
  const fuelStationsEnabled = ref<boolean>(loaded.fuelStationsEnabled)
  const fuelBrandFilter = ref<string[]>(loaded.fuelBrandFilter)
  const fuelTypeFilter = ref<string[]>(loaded.fuelTypeFilter)
  const serviceAreasEnabled = ref<boolean>(loaded.serviceAreasEnabled)

  function save() {
    localStorage.setItem(
      STORAGE_KEY,
      JSON.stringify({
        routeOutlineEnabled: routeOutlineEnabled.value,
        snapToRoadsEnabled: snapToRoadsEnabled.value,
        heatmapEnabled: heatmapEnabled.value,
        speedOverlayEnabled: speedOverlayEnabled.value,
        speedLimitOverlayEnabled: speedLimitOverlayEnabled.value,
        chargingStationsEnabled: chargingStationsEnabled.value,
        chargingMinPowerKw: chargingMinPowerKw.value,
        chargingMaxPowerKw: chargingMaxPowerKw.value,
        fuelStationsEnabled: fuelStationsEnabled.value,
        fuelBrandFilter: fuelBrandFilter.value,
        fuelTypeFilter: fuelTypeFilter.value,
        serviceAreasEnabled: serviceAreasEnabled.value,
      }),
    )
  }
  const scheduleSave = createDebouncedSave(save)

  watch(routeOutlineEnabled, scheduleSave)
  watch(snapToRoadsEnabled, scheduleSave)
  watch(heatmapEnabled, scheduleSave)
  watch(speedOverlayEnabled, scheduleSave)
  watch(speedLimitOverlayEnabled, scheduleSave)
  watch(chargingStationsEnabled, scheduleSave)
  watch(chargingMinPowerKw, scheduleSave)
  watch(chargingMaxPowerKw, scheduleSave)
  watch(fuelStationsEnabled, scheduleSave)
  watch(fuelBrandFilter, scheduleSave, { deep: true })
  watch(fuelTypeFilter, scheduleSave, { deep: true })
  watch(serviceAreasEnabled, scheduleSave)

  return {
    routeOutlineEnabled,
    snapToRoadsEnabled,
    heatmapEnabled,
    speedOverlayEnabled,
    speedLimitOverlayEnabled,
    chargingStationsEnabled,
    chargingMinPowerKw,
    chargingMaxPowerKw,
    fuelStationsEnabled,
    fuelBrandFilter,
    fuelTypeFilter,
    serviceAreasEnabled,
  }
})
