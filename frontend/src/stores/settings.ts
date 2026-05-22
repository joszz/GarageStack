import { defineStore } from 'pinia'
import { ref, watch } from 'vue'
import type { VehicleType } from './vehicle'

export type VehicleTypeOverride = 'auto' | VehicleType
export type Theme = 'dark' | 'light'
export type Locale = 'en' | 'nl'

export type CardId =
  | 'fuelLevel' | 'fuelRange'
  | 'evBattery' | 'charging'
  | 'odometer' | 'battery12v'
  | 'doors' | 'windows'
  | 'climate' | 'hvBattery' | 'findMyCar' | 'lights'
  | 'efficiencyDistance' | 'efficiencyEnergy' | 'efficiencyCharge' | 'efficiencyRatio'

const ALL_CARD_IDS: CardId[] = [
  'fuelLevel', 'fuelRange', 'evBattery', 'charging', 'odometer', 'battery12v',
  'doors', 'windows', 'climate', 'hvBattery', 'findMyCar', 'lights',
  'efficiencyDistance', 'efficiencyEnergy', 'efficiencyCharge', 'efficiencyRatio',
]

export interface CardConfig {
  id: CardId
  visible: boolean
}

export interface AppSettings {
  cards: CardConfig[]
  vehicleTypeOverride: VehicleTypeOverride
  theme: Theme
  showSunRoof: boolean
  locale: Locale
}

function osPreferredTheme(): Theme {
  return window.matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark'
}

function browserLocale(): Locale {
  const lang = navigator.language.toLowerCase()
  return lang.startsWith('nl') ? 'nl' : 'en'
}

export function defaultCards(type: VehicleType | 'unknown' = 'unknown'): CardConfig[] {
  const all: CardConfig[] = [
    { id: 'fuelLevel',          visible: type !== 'bev' },
    { id: 'fuelRange',          visible: type !== 'bev' },
    { id: 'evBattery',          visible: true },
    { id: 'charging',           visible: type === 'phev' || type === 'bev' },
    { id: 'odometer',           visible: true },
    { id: 'battery12v',         visible: true },
    { id: 'doors',              visible: true },
    { id: 'windows',            visible: true },
    { id: 'climate',            visible: true },
    { id: 'hvBattery',          visible: true },
    { id: 'findMyCar',          visible: true },
    { id: 'lights',             visible: true },
    { id: 'efficiencyDistance', visible: true },
    { id: 'efficiencyEnergy',   visible: true },
    { id: 'efficiencyCharge',   visible: type !== 'hev' },
    { id: 'efficiencyRatio',    visible: true },
  ]
  return [...all.filter(c => c.visible), ...all.filter(c => !c.visible)]
}

const BASE_STORAGE_KEY = 'garagestack-settings'

const defaults: AppSettings = {
  cards: defaultCards('unknown'),
  vehicleTypeOverride: 'auto',
  theme: osPreferredTheme(),
  showSunRoof: false,
  locale: browserLocale(),
}

function migrateCards(raw: { id: string; visible: boolean }[]): CardConfig[] {
  const expanded: CardConfig[] = []
  const usedNewIds = new Set<CardId>()
  const rawIds = new Set(raw.map(c => c.id))

  for (const c of raw) {
    switch (c.id) {
      case 'fuel':
        if (!rawIds.has('fuelLevel')) {
          expanded.push({ id: 'fuelLevel', visible: c.visible })
          usedNewIds.add('fuelLevel')
        }
        if (!rawIds.has('fuelRange')) {
          expanded.push({ id: 'fuelRange', visible: c.visible })
          usedNewIds.add('fuelRange')
        }
        break
      case 'doors':
        expanded.push({ id: 'doors', visible: c.visible })
        usedNewIds.add('doors')
        if (!rawIds.has('windows')) {
          expanded.push({ id: 'windows', visible: c.visible })
          usedNewIds.add('windows')
        }
        break
      case 'efficiency':
        if (!rawIds.has('efficiencyDistance')) {
          expanded.push({ id: 'efficiencyDistance', visible: c.visible })
          usedNewIds.add('efficiencyDistance')
        }
        if (!rawIds.has('efficiencyEnergy')) {
          expanded.push({ id: 'efficiencyEnergy', visible: c.visible })
          usedNewIds.add('efficiencyEnergy')
        }
        if (!rawIds.has('efficiencyCharge')) {
          expanded.push({ id: 'efficiencyCharge', visible: c.visible })
          usedNewIds.add('efficiencyCharge')
        }
        if (!rawIds.has('efficiencyRatio')) {
          expanded.push({ id: 'efficiencyRatio', visible: c.visible })
          usedNewIds.add('efficiencyRatio')
        }
        break
      default:
        if ((ALL_CARD_IDS as string[]).includes(c.id)) {
          expanded.push({ id: c.id as CardId, visible: c.visible })
          usedNewIds.add(c.id as CardId)
        }
    }
  }

  for (const id of ALL_CARD_IDS) {
    if (!usedNewIds.has(id)) {
      expanded.push({ id, visible: true })
    }
  }

  return expanded
}

function loadFromKey(key: string): AppSettings {
  try {
    const raw = localStorage.getItem(key)
    if (raw) {
      const parsed = JSON.parse(raw)

      if (parsed.panels && !parsed.cards) {
        const p = parsed.panels
        const visMap: Partial<Record<CardId, boolean>> = {
          fuelLevel:          p.showFuel,
          fuelRange:          p.showFuel,
          evBattery:          p.showEvBattery,
          charging:           p.showCharging,
          hvBattery:          p.showHvPower,
          lights:             p.showLights,
          efficiencyDistance: p.showEfficiency,
          efficiencyEnergy:   p.showEfficiency,
          efficiencyCharge:   p.showEfficiency,
          efficiencyRatio:    p.showEfficiency,
        }
        return {
          cards: defaultCards('unknown').map(c => ({ id: c.id, visible: visMap[c.id] ?? c.visible })),
          vehicleTypeOverride: parsed.vehicleTypeOverride ?? defaults.vehicleTypeOverride,
          theme: parsed.theme ?? defaults.theme,
          showSunRoof: p.showSunRoof ?? defaults.showSunRoof,
          locale: parsed.locale ?? defaults.locale,
        }
      }

      if (Array.isArray(parsed.cards)) {
        return {
          cards: migrateCards(parsed.cards),
          vehicleTypeOverride: parsed.vehicleTypeOverride ?? defaults.vehicleTypeOverride,
          theme: parsed.theme ?? defaults.theme,
          showSunRoof: parsed.showSunRoof ?? defaults.showSunRoof,
          locale: parsed.locale ?? defaults.locale,
        }
      }
    }
  } catch {
    // ignore parse errors
  }

  try {
    const oldRaw = localStorage.getItem('garagestack-panel-settings')
    if (oldRaw) {
      localStorage.removeItem('garagestack-panel-settings')
    }
  } catch {
    // ignore
  }

  return { ...defaults, cards: [...defaults.cards] }
}

export const useSettingsStore = defineStore('settings', () => {
  const storageKey = ref(BASE_STORAGE_KEY)

  const loaded = loadFromKey(storageKey.value)
  const cards = ref<CardConfig[]>(loaded.cards)
  const vehicleTypeOverride = ref<VehicleTypeOverride>(loaded.vehicleTypeOverride)
  const theme = ref<Theme>(loaded.theme)
  const showSunRoof = ref<boolean>(loaded.showSunRoof)
  const locale = ref<Locale>(loaded.locale)

  document.documentElement.dataset.theme = theme.value

  let suppressSave = false

  function save() {
    if (suppressSave) return
    localStorage.setItem(storageKey.value, JSON.stringify({
      cards: cards.value,
      vehicleTypeOverride: vehicleTypeOverride.value,
      theme: theme.value,
      showSunRoof: showSunRoof.value,
      locale: locale.value,
    }))
  }

  watch(cards, save, { deep: true })
  watch(vehicleTypeOverride, save)
  watch(showSunRoof, save)
  watch(locale, save)
  watch(theme, (val) => {
    document.documentElement.dataset.theme = val
    save()
  })

  function loadForUser(userId: number) {
    suppressSave = true
    storageKey.value = `${BASE_STORAGE_KEY}-${userId}`
    const userSettings = loadFromKey(storageKey.value)
    cards.value = userSettings.cards
    vehicleTypeOverride.value = userSettings.vehicleTypeOverride
    theme.value = userSettings.theme
    document.documentElement.dataset.theme = userSettings.theme
    showSunRoof.value = userSettings.showSunRoof
    locale.value = userSettings.locale
    suppressSave = false
    save()
  }

  function resetToGuest() {
    suppressSave = true
    storageKey.value = BASE_STORAGE_KEY
    const guestSettings = loadFromKey(storageKey.value)
    cards.value = guestSettings.cards
    vehicleTypeOverride.value = guestSettings.vehicleTypeOverride
    theme.value = guestSettings.theme
    document.documentElement.dataset.theme = guestSettings.theme
    showSunRoof.value = guestSettings.showSunRoof
    locale.value = guestSettings.locale
    suppressSave = false
  }

  function resetCards(type: VehicleType | 'unknown' = 'unknown') {
    cards.value = defaultCards(type)
  }

  return { cards, vehicleTypeOverride, theme, showSunRoof, locale, resetCards, loadForUser, resetToGuest }
})
