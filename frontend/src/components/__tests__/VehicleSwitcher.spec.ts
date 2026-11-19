import { describe, it, expect, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import { createRouter, createMemoryHistory } from 'vue-router'
import en from '@/locales/en.json'
import type { Vehicle } from '@/services/vehicleApi'
import { useVehicleStore } from '@/stores/vehicle'
import VehicleSwitcher from '../VehicleSwitcher.vue'

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })
const Blank = { template: '<div />' }

function makeRouter() {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', name: 'dashboard', component: Blank },
      { path: '/trip-log', name: 'tripLog', component: Blank },
      { path: '/trips/:id/edit', name: 'tripEdit', component: Blank },
    ],
  })
}

function car(id: number, vin: string, model: string | null): Vehicle {
  return {
    id,
    vin,
    model,
    series: null,
    createdAt: '2026-01-01T00:00:00Z',
    vehicleType: 'bev',
    hvBatteryCapacityKwh: null,
    supportsTargetSoc: false,
    hasPowerTailgate: false,
  }
}

async function mountAt(path: string) {
  const router = makeRouter()
  await router.push(path)
  const wrapper = mount(VehicleSwitcher, {
    global: { plugins: [i18n, router], stubs: { FontAwesomeIcon: true } },
  })
  return { wrapper, router }
}

describe('VehicleSwitcher', () => {
  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
  })

  it("shows just the car's name when there is one car", async () => {
    useVehicleStore().vehicles = [car(1, 'VIN00000000000001', 'MG4')]
    const { wrapper } = await mountAt('/')
    expect(wrapper.find('select').exists()).toBe(false)
    expect(wrapper.text()).toContain('MG4')
  })

  it('offers every car and switches to the one chosen', async () => {
    const store = useVehicleStore()
    store.vehicles = [car(1, 'VIN00000000000001', 'MG HS'), car(2, 'VIN00000000000002', 'MG4')]
    const { wrapper } = await mountAt('/')

    const options = wrapper.findAll('option').map((o) => o.text())
    expect(options).toEqual(['MG HS', 'MG4'])

    await wrapper.find('select').setValue('VIN00000000000002')
    expect(store.activeVin).toBe('VIN00000000000002')
  })

  it('tells two cars of the same model apart by their VIN', async () => {
    useVehicleStore().vehicles = [
      car(1, 'VIN00000000AAAAAA', 'MG4'),
      car(2, 'VIN00000000BBBBBB', 'MG4'),
    ]
    const { wrapper } = await mountAt('/')
    expect(wrapper.findAll('option').map((o) => o.text())).toEqual([
      'MG4 (…AAAAAA)',
      'MG4 (…BBBBBB)',
    ])
  })

  it('leaves a trip being edited, which belongs to the previous car', async () => {
    useVehicleStore().vehicles = [
      car(1, 'VIN00000000000001', 'MG HS'),
      car(2, 'VIN00000000000002', 'MG4'),
    ]
    const { wrapper, router } = await mountAt('/trips/5/edit')

    await wrapper.find('select').setValue('VIN00000000000002')
    await flushPromises()
    expect(router.currentRoute.value.name).toBe('tripLog')
  })
})
