import { describe, it, expect, beforeEach, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import ClimateScheduleFormModal from '../ClimateScheduleFormModal.vue'
import { climateScheduleApi, type ClimateSchedule } from '@/services/climateScheduleApi'
import { useUiSettingsStore } from '@/stores/settingsUi'
import { schedule } from '@/services/__tests__/climateScheduleFixtures'

vi.mock('@/services/climateScheduleApi', () => ({
  climateScheduleApi: {
    list: vi.fn<() => Promise<ClimateSchedule[]>>().mockResolvedValue([]),
    create: vi.fn<() => Promise<ClimateSchedule>>(),
    update: vi.fn<() => Promise<ClimateSchedule>>(),
    delete: vi.fn<() => Promise<void>>().mockResolvedValue(undefined),
  },
}))

const DetailModalStub = {
  name: 'DetailModal',
  template: '<div v-if="open"><slot /><slot name="footer" /></div>',
  props: ['open', 'title', 'wide'],
  emits: ['close'],
}

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

async function openForm(existing: ClimateSchedule | null = null) {
  const wrapper = mount(ClimateScheduleFormModal, {
    props: { open: false, vin: 'FAKEVN00000000001', schedule: existing },
    global: { plugins: [i18n], stubs: { DetailModal: DetailModalStub, FontAwesomeIcon: true } },
  })
  // The form fills itself in when it opens.
  await wrapper.setProps({ open: true })
  return wrapper
}

function button(wrapper: Awaited<ReturnType<typeof openForm>>, label: string) {
  return wrapper.findAll('button').find((b) => b.text() === label)!
}

async function save(wrapper: Awaited<ReturnType<typeof openForm>>) {
  await button(wrapper, 'Save schedule').trigger('click')
  await flushPromises()
}

describe('ClimateScheduleFormModal', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    vi.mocked(climateScheduleApi.create).mockResolvedValue(schedule())
    vi.mocked(climateScheduleApi.update).mockResolvedValue(schedule())
  })

  it('saves a new schedule in the browser time zone, with its days as ISO weekdays in order', async () => {
    const wrapper = await openForm()

    await button(wrapper, 'Sat').trigger('click')
    await button(wrapper, 'Mon').trigger('click')
    await save(wrapper)

    expect(climateScheduleApi.create).toHaveBeenCalledWith('FAKEVN00000000001', {
      name: null,
      enabled: true,
      startTime: '07:30',
      days: [2, 3, 4, 5, 6],
      timeZoneId: Intl.DateTimeFormat().resolvedOptions().timeZone,
      mode: 'on',
      temperatureC: 21,
      rearDefroster: false,
      seatLeftLevel: 0,
      seatRightLevel: 0,
      onlyBelowC: null,
      onlyAboveC: null,
    })
    expect(wrapper.emitted('close')).toHaveLength(1)
  })

  it('takes no temperature for fan only', async () => {
    const wrapper = await openForm()

    await button(wrapper, 'Fan only').trigger('click')

    expect(wrapper.text()).not.toContain('AC temperature')
  })

  it('saves an outside temperature typed in Fahrenheit as Celsius', async () => {
    useUiSettingsStore().units.temperature = 'fahrenheit'
    const wrapper = await openForm()

    await wrapper.find('#climate-schedule-condition').setValue(true)
    expect((wrapper.find('#climate-schedule-below').element as HTMLInputElement).value).toBe('41')
    await wrapper.find('#climate-schedule-below').setValue('32')
    await save(wrapper)

    const request = vi.mocked(climateScheduleApi.create).mock.calls[0]![1]
    expect(request.onlyBelowC).toBeCloseTo(0)
    expect(request.onlyAboveC).toBeNull()
  })

  it('keeps an edited schedule in its own time zone, and an untouched threshold exactly', async () => {
    useUiSettingsStore().units.temperature = 'fahrenheit'
    const tokyo = schedule({ id: 4, timeZoneId: 'Asia/Tokyo', onlyBelowC: 4.4 })
    const wrapper = await openForm(tokyo)

    expect(wrapper.text()).toContain('Runs on Asia/Tokyo time')
    await save(wrapper)

    const [, id, request] = vi.mocked(climateScheduleApi.update).mock.calls[0]!
    expect(id).toBe(4)
    expect(request.timeZoneId).toBe('Asia/Tokyo')
    expect(request.onlyBelowC).toBe(4.4)
  })

  it('refuses a condition without an outside temperature', async () => {
    const wrapper = await openForm()

    await wrapper.find('#climate-schedule-condition').setValue(true)
    await wrapper.find('#climate-schedule-below').setValue('')
    await save(wrapper)

    expect(climateScheduleApi.create).not.toHaveBeenCalled()
    expect(wrapper.find('[role="alert"]').text()).toBe('Enter at least one outside temperature.')
  })

  it('asks before deleting a schedule', async () => {
    const wrapper = await openForm(
      schedule({ id: 9, lastRunOutcome: 'failed', lastRunDetail: 'vehicle offline' }),
    )

    expect(wrapper.text()).toContain('Failed')
    expect(wrapper.text()).toContain('vehicle offline')
    await button(wrapper, 'Delete').trigger('click')
    expect(wrapper.text()).toContain('Delete this schedule?')
    expect(climateScheduleApi.delete).not.toHaveBeenCalled()

    await button(wrapper, 'Confirm').trigger('click')
    await flushPromises()

    expect(climateScheduleApi.delete).toHaveBeenCalledWith('FAKEVN00000000001', 9)
    expect(wrapper.emitted('close')).toHaveLength(1)
  })
})
