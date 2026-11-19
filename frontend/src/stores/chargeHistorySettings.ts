import { defineStore } from 'pinia'
import { reactive, toRefs } from 'vue'
import { loadStoredSettings, persistSettings } from './settingsShared'

const STORAGE_KEY = 'garagestack-charge-history-settings'

export interface ChargeHistorySettings {
  defaultRatePerKwh: number | null
  manualSessionPrices: Record<string, number>
}

function isPrice(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value) && value >= 0
}

function parseSettings(raw: Record<string, unknown>): ChargeHistorySettings {
  const manualSessionPrices: Record<string, number> = {}
  const prices = raw.manualSessionPrices
  if (typeof prices === 'object' && prices !== null && !Array.isArray(prices)) {
    for (const [key, value] of Object.entries(prices)) {
      if (key.length > 0 && isPrice(value)) manualSessionPrices[key] = value
    }
  }
  return {
    defaultRatePerKwh: isPrice(raw.defaultRatePerKwh) ? raw.defaultRatePerKwh : null,
    manualSessionPrices,
  }
}

export function chargeSessionPriceKey(vin: string, startedAt: string): string {
  return `${vin}:${startedAt}`
}

export const useChargeHistorySettingsStore = defineStore('chargeHistorySettings', () => {
  const state = reactive(
    loadStoredSettings(STORAGE_KEY, parseSettings, () => ({
      defaultRatePerKwh: null,
      manualSessionPrices: {},
    })),
  )

  persistSettings({
    storageKey: STORAGE_KEY,
    section: 'chargeHistory',
    state,
    parse: parseSettings,
  })

  function setManualSessionPrice(vin: string, startedAt: string, price: number): void {
    if (!isPrice(price)) throw new RangeError('Session price must be a non-negative number')
    state.manualSessionPrices[chargeSessionPriceKey(vin, startedAt)] = price
  }

  function clearManualSessionPrice(vin: string, startedAt: string): void {
    delete state.manualSessionPrices[chargeSessionPriceKey(vin, startedAt)]
  }

  return {
    ...toRefs(state),
    setManualSessionPrice,
    clearManualSessionPrice,
  }
})
