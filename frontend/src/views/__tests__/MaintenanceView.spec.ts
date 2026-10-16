import { describe, it, expect, vi, beforeEach } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import MaintenanceView from '../MaintenanceView.vue'
import { maintenanceApi, type MaintenanceItem } from '@/services/maintenanceApi'
import { vehicleApi, type Vehicle } from '@/services/vehicleApi'
import { maintenanceItem } from '@/services/__tests__/maintenanceFixtures'

vi.mock('@/services/maintenanceApi', () => ({
  maintenanceApi: { list: vi.fn<() => Promise<MaintenanceItem[]>>() },
}))

vi.mock('@/services/vehicleApi', () => ({
  vehicleApi: { list: vi.fn<() => Promise<Vehicle[]>>() },
}))

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

const car = { id: 1, vin: 'FAKEVN00000000001', vehicleType: 'bev' } as Vehicle

async function mountView() {
  const wrapper = mount(MaintenanceView, {
    global: {
      plugins: [i18n],
      stubs: {
        FontAwesomeIcon: true,
        MaintenanceItemFormModal: true,
        MaintenanceItemDetailModal: true,
        StatusCard: {
          props: ['label', 'variant'],
          template: '<div class="item" :data-variant="variant">{{ label }}</div>',
        },
      },
    },
  })
  await flushPromises()
  return wrapper
}

describe('MaintenanceView', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    vi.mocked(vehicleApi.list).mockResolvedValue([car])
    vi.mocked(maintenanceApi.list).mockResolvedValue([])
  })

  it("loads the car's items, the most urgent first", async () => {
    vi.mocked(maintenanceApi.list).mockResolvedValue([
      maintenanceItem({ id: 1, name: 'Tyres', dueStatus: 'ok' }),
      maintenanceItem({ id: 2, name: 'Oil', dueStatus: 'overdue' }),
      maintenanceItem({ id: 3, name: 'Brakes', dueStatus: 'dueSoon' }),
    ])

    const wrapper = await mountView()

    expect(maintenanceApi.list).toHaveBeenCalledWith('FAKEVN00000000001')
    expect(wrapper.findAll('.item').map((i) => [i.text(), i.attributes('data-variant')])).toEqual([
      ['Oil', 'danger'],
      ['Brakes', 'warning'],
      ['Tyres', 'success'],
    ])
  })

  it('says so when there are no items yet', async () => {
    const wrapper = await mountView()

    expect(wrapper.text()).toContain(en.maintenance.empty)
  })

  it('says why the list could not be loaded', async () => {
    vi.mocked(maintenanceApi.list).mockRejectedValue(new TypeError('Failed to fetch'))

    const wrapper = await mountView()

    expect(wrapper.text()).toContain(en.errors.offline)
  })

  // Without a car there is nothing to add an item to, and no form to open.
  it('only offers to add an item once a car is known', async () => {
    vi.mocked(vehicleApi.list).mockResolvedValue([])

    const wrapper = await mountView()

    expect(wrapper.get('.view-header__actions button').attributes('disabled')).toBeDefined()
  })
})
