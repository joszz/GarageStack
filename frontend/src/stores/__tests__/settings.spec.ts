import { describe, it, expect, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { nextTick } from 'vue'
import { defaultCards, useSettingsStore } from '@/stores/settings'

const BASE_KEY = 'garagestack-settings'

describe('defaultCards', () => {
  it('includes all 17 card ids', () => {
    expect(defaultCards()).toHaveLength(17)
  })

  it('places visible cards before hidden ones', () => {
    const cards = defaultCards('bev')
    const firstHidden = cards.findIndex(c => !c.visible)
    if (firstHidden !== -1) {
      expect(cards.slice(firstHidden).some(c => c.visible)).toBe(false)
    }
  })

  it('hev: hides charging and efficiencyCharge, shows fuel', () => {
    const cards = defaultCards('hev')
    expect(cards.find(c => c.id === 'fuelLevel')!.visible).toBe(true)
    expect(cards.find(c => c.id === 'charging')!.visible).toBe(false)
    expect(cards.find(c => c.id === 'efficiencyCharge')!.visible).toBe(false)
  })

  it('bev: hides fuel cards, shows charging and efficiencyCharge', () => {
    const cards = defaultCards('bev')
    expect(cards.find(c => c.id === 'fuelLevel')!.visible).toBe(false)
    expect(cards.find(c => c.id === 'fuelRange')!.visible).toBe(false)
    expect(cards.find(c => c.id === 'charging')!.visible).toBe(true)
    expect(cards.find(c => c.id === 'efficiencyCharge')!.visible).toBe(true)
  })

  it('phev: shows both fuel and charging', () => {
    const cards = defaultCards('phev')
    expect(cards.find(c => c.id === 'fuelLevel')!.visible).toBe(true)
    expect(cards.find(c => c.id === 'charging')!.visible).toBe(true)
  })

  it('unknown: hides charging, shows fuel', () => {
    const cards = defaultCards('unknown')
    expect(cards.find(c => c.id === 'charging')!.visible).toBe(false)
    expect(cards.find(c => c.id === 'fuelLevel')!.visible).toBe(true)
  })
})

describe('useSettingsStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
  })

  it('defaults to dark theme when matchMedia reports no light preference', () => {
    const store = useSettingsStore()
    expect(store.theme).toBe('dark')
  })

  it('persists theme change to localStorage', async () => {
    const store = useSettingsStore()
    store.theme = 'light'
    await nextTick()
    const saved = JSON.parse(localStorage.getItem(BASE_KEY)!)
    expect(saved.theme).toBe('light')
  })

  it('persists locale change to localStorage', async () => {
    const store = useSettingsStore()
    store.locale = 'nl'
    await nextTick()
    const saved = JSON.parse(localStorage.getItem(BASE_KEY)!)
    expect(saved.locale).toBe('nl')
  })

  it('persists card visibility changes to localStorage', async () => {
    const store = useSettingsStore()
    const firstCard = store.cards[0]
    expect(firstCard).toBeDefined()
    if (!firstCard) throw new Error('Expected a first card to exist')
    firstCard.visible = !firstCard.visible
    await nextTick()
    const saved = JSON.parse(localStorage.getItem(BASE_KEY)!)
    const savedFirstCard = saved.cards[0]
    expect(savedFirstCard).toBeDefined()
    if (!savedFirstCard) throw new Error('Expected a saved first card to exist')
    expect(savedFirstCard.visible).toBe(firstCard.visible)
  })

  it('loads all settings from localStorage on init', () => {
    localStorage.setItem(BASE_KEY, JSON.stringify({
      cards: defaultCards('bev'),
      vehicleTypeOverride: 'bev',
      theme: 'light',
      locale: 'nl',
    }))
    const store = useSettingsStore()
    expect(store.theme).toBe('light')
    expect(store.locale).toBe('nl')
    expect(store.vehicleTypeOverride).toBe('bev')
  })

  it('falls back to defaults when localStorage is empty', () => {
    const store = useSettingsStore()
    expect(store.vehicleTypeOverride).toBe('auto')
    expect(store.cards).toHaveLength(17)
    expect(store.cards.find(c => c.id === 'sunRoof')!.visible).toBe(false)
  })

  describe('card migration', () => {
    it('expands legacy fuel card into fuelLevel + fuelRange with same visibility', () => {
      localStorage.setItem(BASE_KEY, JSON.stringify({
        cards: [{ id: 'fuel', visible: false }, { id: 'odometer', visible: true }],
        theme: 'dark',
      }))
      const store = useSettingsStore()
      expect(store.cards.find(c => c.id === 'fuelLevel')!.visible).toBe(false)
      expect(store.cards.find(c => c.id === 'fuelRange')!.visible).toBe(false)
      expect(store.cards.find(c => c.id === 'odometer')!.visible).toBe(true)
    })

    it('expands legacy efficiency card into four efficiency cards', () => {
      localStorage.setItem(BASE_KEY, JSON.stringify({
        cards: [{ id: 'efficiency', visible: false }],
        theme: 'dark',
      }))
      const store = useSettingsStore()
      expect(store.cards.find(c => c.id === 'efficiencyDistance')!.visible).toBe(false)
      expect(store.cards.find(c => c.id === 'efficiencyEnergy')!.visible).toBe(false)
      expect(store.cards.find(c => c.id === 'efficiencyCharge')!.visible).toBe(false)
      expect(store.cards.find(c => c.id === 'efficiencyRatio')!.visible).toBe(false)
    })

    it('migrates legacy doors card and inserts windows alongside it', () => {
      localStorage.setItem(BASE_KEY, JSON.stringify({
        cards: [{ id: 'doors', visible: false }],
        theme: 'dark',
      }))
      const store = useSettingsStore()
      expect(store.cards.find(c => c.id === 'doors')!.visible).toBe(false)
      expect(store.cards.find(c => c.id === 'windows')).toBeTruthy()
    })

    it('migrates the old panels object format', () => {
      localStorage.setItem(BASE_KEY, JSON.stringify({
        panels: {
          showFuel: false,
          showEvBattery: true,
          showCharging: false,
          showHvPower: true,
          showLights: false,
          showEfficiency: true,
          showSunRoof: true,
        },
        theme: 'light',
      }))
      const store = useSettingsStore()
      expect(store.cards.find(c => c.id === 'fuelLevel')!.visible).toBe(false)
      expect(store.cards.find(c => c.id === 'fuelRange')!.visible).toBe(false)
      expect(store.cards.find(c => c.id === 'efficiencyDistance')!.visible).toBe(true)
      expect(store.cards.find(c => c.id === 'sunRoof')!.visible).toBe(true)
      expect(store.theme).toBe('light')
    })

    it('appends newly introduced cards using their default visibility when migrating old data', () => {
      // A saved list that only has known-new ids but is missing some
      localStorage.setItem(BASE_KEY, JSON.stringify({
        cards: [{ id: 'odometer', visible: true }],
        theme: 'dark',
      }))
      const store = useSettingsStore()
      // All 17 card ids should be present after migration fills in the gaps
      expect(store.cards).toHaveLength(17)
      expect(store.cards.find(c => c.id === 'sunRoof')!.visible).toBe(false)
    })
  })

  describe('per-user storage', () => {
    it('writes to a user-scoped key after loadForUser', async () => {
      const store = useSettingsStore()
      store.loadForUser(42)
      store.theme = 'light'
      await nextTick()
      const saved = JSON.parse(localStorage.getItem(`${BASE_KEY}-42`)!)
      expect(saved.theme).toBe('light')
    })

    it('does not write to the base key while a user is loaded', async () => {
      const store = useSettingsStore()
      const before = localStorage.getItem(BASE_KEY)
      store.loadForUser(42)
      store.theme = 'light'
      await nextTick()
      expect(localStorage.getItem(BASE_KEY)).toBe(before)
    })

    it('reverts to base key settings after resetToGuest', async () => {
      localStorage.setItem(BASE_KEY, JSON.stringify({
        cards: defaultCards(),
        vehicleTypeOverride: 'auto',
        theme: 'dark',
        locale: 'en',
      }))
      const store = useSettingsStore()
      store.loadForUser(42)
      store.theme = 'light'
      await nextTick()
      store.resetToGuest()
      expect(store.theme).toBe('dark')
    })
  })
})
