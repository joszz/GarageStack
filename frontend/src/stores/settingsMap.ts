import { defineStore } from 'pinia'
import { ref, watch } from 'vue'
import { readLegacyBlob, createDebouncedSave } from './settingsShared'

const STORAGE_KEY = 'garagestack-settings-map'

interface MapSettings {
  routeOutlineEnabled: boolean
  heatmapEnabled: boolean
  speedOverlayEnabled: boolean
  chargingStationsEnabled: boolean
  chargingMinPowerKw: number
  chargingMaxPowerKw: number
  fuelStationsEnabled: boolean
  fuelBrandFilter: string[]
  serviceAreasEnabled: boolean
}

const defaults: MapSettings = {
  routeOutlineEnabled: false,
  heatmapEnabled: true,
  speedOverlayEnabled: false,
  chargingStationsEnabled: false,
  chargingMinPowerKw: 0,
  chargingMaxPowerKw: 0,
  fuelStationsEnabled: false,
  fuelBrandFilter: [],
  serviceAreasEnabled: false,
}

function parseMapFields(parsed: Record<string, unknown>): MapSettings {
  return {
    routeOutlineEnabled: parsed.routeOutlineEnabled === true,
    heatmapEnabled: parsed.heatmapEnabled !== false,
    speedOverlayEnabled: parsed.speedOverlayEnabled === true,
    chargingStationsEnabled: parsed.chargingStationsEnabled === true,
    chargingMinPowerKw:
      typeof parsed.chargingMinPowerKw === 'number' ? parsed.chargingMinPowerKw : 0,
    chargingMaxPowerKw:
      typeof parsed.chargingMaxPowerKw === 'number' ? parsed.chargingMaxPowerKw : 0,
    fuelStationsEnabled: parsed.fuelStationsEnabled === true,
    fuelBrandFilter: Array.isArray(parsed.fuelBrandFilter)
      ? (parsed.fuelBrandFilter as string[])
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
  const heatmapEnabled = ref<boolean>(loaded.heatmapEnabled)
  const speedOverlayEnabled = ref<boolean>(loaded.speedOverlayEnabled)
  const chargingStationsEnabled = ref<boolean>(loaded.chargingStationsEnabled)
  const chargingMinPowerKw = ref<number>(loaded.chargingMinPowerKw)
  const chargingMaxPowerKw = ref<number>(loaded.chargingMaxPowerKw)
  const fuelStationsEnabled = ref<boolean>(loaded.fuelStationsEnabled)
  const fuelBrandFilter = ref<string[]>(loaded.fuelBrandFilter)
  const serviceAreasEnabled = ref<boolean>(loaded.serviceAreasEnabled)

  function save() {
    localStorage.setItem(
      STORAGE_KEY,
      JSON.stringify({
        routeOutlineEnabled: routeOutlineEnabled.value,
        heatmapEnabled: heatmapEnabled.value,
        speedOverlayEnabled: speedOverlayEnabled.value,
        chargingStationsEnabled: chargingStationsEnabled.value,
        chargingMinPowerKw: chargingMinPowerKw.value,
        chargingMaxPowerKw: chargingMaxPowerKw.value,
        fuelStationsEnabled: fuelStationsEnabled.value,
        fuelBrandFilter: fuelBrandFilter.value,
        serviceAreasEnabled: serviceAreasEnabled.value,
      }),
    )
  }
  const scheduleSave = createDebouncedSave(save)

  watch(routeOutlineEnabled, scheduleSave)
  watch(heatmapEnabled, scheduleSave)
  watch(speedOverlayEnabled, scheduleSave)
  watch(chargingStationsEnabled, scheduleSave)
  watch(chargingMinPowerKw, scheduleSave)
  watch(chargingMaxPowerKw, scheduleSave)
  watch(fuelStationsEnabled, scheduleSave)
  watch(fuelBrandFilter, scheduleSave, { deep: true })
  watch(serviceAreasEnabled, scheduleSave)

  return {
    routeOutlineEnabled,
    heatmapEnabled,
    speedOverlayEnabled,
    chargingStationsEnabled,
    chargingMinPowerKw,
    chargingMaxPowerKw,
    fuelStationsEnabled,
    fuelBrandFilter,
    serviceAreasEnabled,
  }
})
