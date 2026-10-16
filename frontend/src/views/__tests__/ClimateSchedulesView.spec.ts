import { describe, it, expect, vi, beforeEach } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import ClimateSchedulesView from '../ClimateSchedulesView.vue'
import { climateScheduleApi, type ClimateSchedule } from '@/services/climateScheduleApi'
import { vehicleApi, type Vehicle } from '@/services/vehicleApi'
import { schedule } from '@/services/__tests__/climateScheduleFixtures'

vi.mock('@/services/climateScheduleApi', () => ({
  climateScheduleApi: { list: vi.fn<() => Promise<ClimateSchedule[]>>() },
}))

vi.mock('@/services/vehicleApi', () => ({
  vehicleApi: { list: vi.fn<() => Promise<Vehicle[]>>() },
}))

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

const car = { id: 1, vin: 'FAKEVN00000000001', vehicleType: 'bev' } as Vehicle

async function mountView() {
  const wrapper = mount(ClimateSchedulesView, {
    global: {
      plugins: [i18n],
      stubs: {
        FontAwesomeIcon: true,
        ClimateScheduleFormModal: true,
        StatusCard: {
          props: ['label', 'value', 'variant'],
          template:
            '<div class="schedule" :data-variant="variant ?? \'none\'">{{ label }}|{{ value }}</div>',
        },
      },
    },
  })
  await flushPromises()
  return wrapper
}

describe('ClimateSchedulesView', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    vi.mocked(vehicleApi.list).mockResolvedValue([car])
    vi.mocked(climateScheduleApi.list).mockResolvedValue([])
  })

  it("lists the car's schedules by name and days, with their start time", async () => {
    vi.mocked(climateScheduleApi.list).mockResolvedValue([
      schedule({ name: 'Work', startTime: '07:30' }),
    ])

    const wrapper = await mountView()

    expect(climateScheduleApi.list).toHaveBeenCalledWith('FAKEVN00000000001')
    const row = wrapper.get('.schedule').text()
    expect(row.startsWith('Work · ')).toBe(true)
    expect(row).toContain('07:30')
  })

  // A schedule whose last run went wrong stands out; one switched off does not compete for it.
  it('marks a schedule whose last run needs a look, and leaves one that is off plain', async () => {
    vi.mocked(climateScheduleApi.list).mockResolvedValue([
      schedule({ id: 1, name: 'Fine', lastRunOutcome: 'started' }),
      schedule({ id: 2, name: 'Refused', lastRunOutcome: 'failed' }),
      schedule({ id: 3, name: 'Off', enabled: false, lastRunOutcome: 'failed' }),
    ])

    const wrapper = await mountView()

    expect(wrapper.findAll('.schedule').map((s) => s.attributes('data-variant'))).toEqual([
      'info',
      'warning',
      'none',
    ])
  })

  it('says so when there are no schedules yet', async () => {
    const wrapper = await mountView()

    expect(wrapper.text()).toContain(en.climateSchedules.empty)
  })

  it('only offers to add a schedule once a car is known', async () => {
    vi.mocked(vehicleApi.list).mockResolvedValue([])

    const wrapper = await mountView()

    expect(wrapper.get('.view-header__actions button').attributes('disabled')).toBeDefined()
    expect(climateScheduleApi.list).not.toHaveBeenCalled()
  })
})
