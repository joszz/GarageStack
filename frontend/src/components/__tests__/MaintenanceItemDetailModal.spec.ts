import { describe, it, expect, beforeEach, vi, afterEach } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import MaintenanceItemDetailModal from '../MaintenanceItemDetailModal.vue'
import {
  maintenanceApi,
  type LogMaintenanceServiceResponse,
  type MaintenanceCost,
  type MaintenanceItem,
  type MaintenanceLogEntry,
} from '@/services/maintenanceApi'
import { useUiSettingsStore } from '@/stores/settingsUi'
import { ApiError } from '@/services/apiCore'
import { maintenanceItem } from '@/services/__tests__/maintenanceFixtures'
import { DetailModalStub } from './stubs'
import { resetConnection } from '@/composables/useConnection'
import { setBrowserOnline } from '@/composables/__tests__/connectionStub'

vi.mock('@/services/maintenanceApi', () => ({
  maintenanceApi: {
    list: vi.fn<() => Promise<MaintenanceItem[]>>().mockResolvedValue([]),
    create: vi.fn<() => Promise<MaintenanceItem>>(),
    update: vi.fn<() => Promise<MaintenanceItem>>(),
    delete: vi.fn<() => Promise<void>>().mockResolvedValue(undefined),
    listLog: vi.fn<() => Promise<MaintenanceLogEntry[]>>().mockResolvedValue([]),
    logService: vi.fn<() => Promise<LogMaintenanceServiceResponse>>(),
    deleteLogEntry: vi.fn<() => Promise<void>>().mockResolvedValue(undefined),
    listCosts: vi.fn<() => Promise<MaintenanceCost[]>>().mockResolvedValue([]),
  },
}))

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

const entry: MaintenanceLogEntry = {
  id: 7,
  maintenanceItemId: 1,
  performedAt: '2026-09-01',
  odometerKm: 12000,
  notes: null,
  cost: null,
  createdAt: '2026-09-01',
}

async function openModal() {
  const wrapper = mount(MaintenanceItemDetailModal, {
    props: { open: false, vin: 'FAKEVN00000000001', item: maintenanceItem() },
    global: { plugins: [i18n], stubs: { DetailModal: DetailModalStub, FontAwesomeIcon: true } },
  })
  await wrapper.setProps({ open: true })
  await flushPromises()
  return wrapper
}

function button(wrapper: Awaited<ReturnType<typeof openModal>>, label: string) {
  return wrapper.findAll('button').find((b) => b.text() === label)!
}

async function deleteItem(wrapper: Awaited<ReturnType<typeof openModal>>) {
  await button(wrapper, 'Delete').trigger('click')
  await button(wrapper, 'Confirm').trigger('click')
  await flushPromises()
}

describe('MaintenanceItemDetailModal', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('closes once the item is deleted', async () => {
    const wrapper = await openModal()

    await deleteItem(wrapper)

    expect(maintenanceApi.delete).toHaveBeenCalledWith('FAKEVN00000000001', 1)
    expect(wrapper.emitted('close')).toHaveLength(1)
  })

  it('stays open and says why when the delete is refused', async () => {
    vi.mocked(maintenanceApi.delete).mockRejectedValueOnce(new ApiError(500, 'Server error'))
    const wrapper = await openModal()

    await deleteItem(wrapper)

    expect(wrapper.emitted('close')).toBeUndefined()
    expect(wrapper.find('.text-danger').text()).toBe(en.errors.server)
  })

  // No undo, and it moves the item's last service back: the first tap only asks.
  it('deletes a history entry on the second tap only', async () => {
    vi.mocked(maintenanceApi.listLog).mockResolvedValue([entry])
    vi.mocked(maintenanceApi.deleteLogEntry).mockResolvedValue(undefined)
    const wrapper = await openModal()
    const button = wrapper.find('.maintenance-history__delete')

    await button.trigger('click')
    await flushPromises()
    expect(maintenanceApi.deleteLogEntry).not.toHaveBeenCalled()
    expect(button.classes()).toContain('btn-danger')
    expect(button.attributes('aria-label')).toBe(en.maintenance.confirmDeleteLogEntryLabel)

    await button.trigger('click')
    await flushPromises()
    expect(maintenanceApi.deleteLogEntry).toHaveBeenCalledOnce()
  })

  it('logs a service with its cost, and none when the field is left empty', async () => {
    const logged = { ...entry, id: 8, cost: 89.95 }
    vi.mocked(maintenanceApi.logService).mockResolvedValue({
      item: maintenanceItem(),
      logEntry: logged,
    })
    const wrapper = await openModal()

    await wrapper.get('#log-cost').setValue('89.95')
    await wrapper.get('.maintenance-log-form').trigger('submit')
    await flushPromises()
    expect(vi.mocked(maintenanceApi.logService).mock.calls[0]![2]).toMatchObject({ cost: 89.95 })
    expect(maintenanceApi.listCosts).toHaveBeenCalledOnce()
    expect((wrapper.get('#log-cost').element as HTMLInputElement).value).toBe('')

    await wrapper.get('.maintenance-log-form').trigger('submit')
    await flushPromises()
    expect(vi.mocked(maintenanceApi.logService).mock.calls[1]![2]).toMatchObject({ cost: null })
  })

  it('shows what a service cost in the chosen currency', async () => {
    useUiSettingsStore().currency = 'GBP'
    vi.mocked(maintenanceApi.listLog).mockResolvedValue([{ ...entry, cost: 412 }])
    const wrapper = await openModal()

    expect(wrapper.get('.maintenance-history__row').text()).toContain('£412.00')
    expect(wrapper.get('label[for="log-cost"]').text()).toBe('Cost (£)')
  })

  it('says why when a history entry cannot be deleted', async () => {
    vi.mocked(maintenanceApi.listLog).mockResolvedValue([entry])
    vi.mocked(maintenanceApi.deleteLogEntry).mockRejectedValueOnce(new TypeError('offline'))
    const wrapper = await openModal()

    await wrapper.find('.maintenance-history__delete').trigger('click')
    await wrapper.find('.maintenance-history__delete').trigger('click')
    await flushPromises()

    expect(wrapper.find('.text-danger').text()).toBe(en.errors.offline)
    expect(wrapper.findAll('.maintenance-history__row')).toHaveLength(1)
  })
})

describe('MaintenanceItemDetailModal without the server', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    setBrowserOnline(false)
  })
  afterEach(() => {
    vi.restoreAllMocks()
    resetConnection()
  })

  it('shows the item and its history without logging, editing or deleting', async () => {
    vi.mocked(maintenanceApi.listLog).mockResolvedValueOnce([entry])
    const wrapper = await openModal()

    expect(wrapper.text()).toContain(en.maintenance.history)
    expect(wrapper.find('.maintenance-log-form').exists()).toBe(false)
    expect(wrapper.find('.maintenance-history__delete').exists()).toBe(false)
    expect(wrapper.findAll('button').map((b) => b.text())).not.toContain(en.maintenance.editItem)
  })
})
