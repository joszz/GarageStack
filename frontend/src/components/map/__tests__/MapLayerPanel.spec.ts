import { beforeEach, describe, expect, it } from 'vitest'
import { enableAutoUnmount, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import { useMapSettingsStore } from '@/stores/settingsMap'
import MapLayerPanel from '../MapLayerPanel.vue'

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

enableAutoUnmount(beforeEach)

beforeEach(() => {
  localStorage.clear()
  setActivePinia(createPinia())
})

async function mountPanel(phoneGpsAvailable: boolean) {
  const wrapper = mount(MapLayerPanel, {
    props: {
      carTakesFuel: false,
      carTakesCharge: false,
      speedCamerasAvailable: false,
      phoneGpsAvailable,
    },
    attachTo: document.body,
    global: { plugins: [i18n], stubs: { Teleport: true, FontAwesomeIcon: true } },
  })
  await wrapper.get('.toolbar-panel__btn').trigger('click')
  return wrapper
}

// The heatmap and snapping are on by default, so the badge starts at two without phone GPS.
const badge = (wrapper: Awaited<ReturnType<typeof mountPanel>>) =>
  wrapper.get('.toolbar-panel__count').text()

describe('MapLayerPanel', () => {
  it('offers phone GPS as a layer once a phone added points, on by default', async () => {
    const wrapper = await mountPanel(true)

    const toggle = wrapper.find<HTMLInputElement>(
      '.settings-toggle input[type="checkbox"][aria-label="Phone GPS"]',
    )
    expect(wrapper.text()).toContain(en.trips.phoneGpsDesc)
    expect(toggle.element.checked).toBe(true)
    expect(badge(wrapper)).toBe('3')

    await toggle.setValue(false)

    expect(useMapSettingsStore().phoneGpsEnabled).toBe(false)
    expect(badge(wrapper)).toBe('2')
  })

  it('offers car parks, car washes, air pumps and garages to every car, and counts them', async () => {
    const wrapper = await mountPanel(false)
    const toggle = (label: string) =>
      wrapper.get<HTMLInputElement>(
        `.settings-toggle input[type="checkbox"][aria-label="${label}"]`,
      )

    for (const label of [
      en.poi.parking.layer,
      en.poi.carWash.layer,
      en.poi.air.layer,
      en.poi.repair.layer,
    ])
      expect(toggle(label).element.checked).toBe(false)

    await toggle(en.poi.air.layer).setValue(true)

    expect(useMapSettingsStore().airPumpsEnabled).toBe(true)
    expect(badge(wrapper)).toBe('3')
  })

  it('groups the layers by what they draw on, one tab at a time', async () => {
    const wrapper = mount(MapLayerPanel, {
      props: {
        carTakesFuel: true,
        carTakesCharge: true,
        speedCamerasAvailable: true,
        phoneGpsAvailable: true,
      },
      attachTo: document.body,
      global: { plugins: [i18n], stubs: { Teleport: true, FontAwesomeIcon: true } },
    })
    await wrapper.get('.toolbar-panel__btn').trigger('click')
    const tabs = wrapper.findAll('[role=tab]')
    const panel = (name: string) => wrapper.get(`[id$="-panel-${name}"]`)
    const labels = (name: string) =>
      panel(name)
        .findAll('input[role=switch]')
        .map((input) => input.attributes('aria-label'))

    expect(tabs.map((tab) => tab.text())).toEqual(['Routes', 'Trip', 'Places'])
    expect(tabs.map((tab) => tab.attributes('aria-label'))).toEqual([
      en.trips.layerTabs.routes,
      en.trips.layerTabs.trip,
      en.trips.layerTabs.places,
    ])
    expect(labels('routes')).toEqual([en.trips.heatmap, en.trips.routeOutline, en.trips.phoneGps])
    expect(labels('trip')).toEqual([
      en.trips.snapToRoads,
      en.trips.speedOverlay,
      en.trips.speedLimitOverlay,
    ])
    expect(labels('places')).toEqual([
      en.trips.fuelStations,
      en.trips.chargingStations,
      en.trips.serviceAreas,
      en.trips.speedCameras,
      en.poi.parking.layer,
      en.poi.carWash.layer,
      en.poi.air.layer,
      en.poi.repair.layer,
    ])
    expect(wrapper.findAll('[role=tabpanel]').filter((item) => item.isVisible())).toHaveLength(1)
    expect(panel('routes').isVisible()).toBe(true)

    await tabs[2]!.trigger('click')

    expect(panel('places').isVisible()).toBe(true)
    expect(panel('routes').isVisible()).toBe(false)
    expect(panel('places').attributes('aria-labelledby')).toBe(tabs[2]!.attributes('id'))
  })

  it('offers speed limits beside snapping, and only while snapping is on', async () => {
    const wrapper = await mountPanel(false)
    const trip = wrapper.get('[id$="-panel-trip"]')

    const snap = trip.get(`input[aria-label="${en.trips.snapToRoads}"]`)
    expect(trip.text()).toContain(en.trips.speedLimitOverlay)
    await snap.setValue(false)

    expect(trip.text()).not.toContain(en.trips.speedLimitOverlay)
    // Patched in place, not rebuilt, so the switch just pressed keeps the keyboard focus.
    expect(snap.element.isConnected).toBe(true)
  })

  it('leaves the layer out, and out of the count, without a location service', async () => {
    const wrapper = await mountPanel(false)

    expect(wrapper.text()).not.toContain(en.trips.phoneGps)
    expect(badge(wrapper)).toBe('2')
  })
})
