import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import type { LogPage, LogQuery } from '@/services/logsApi'
import LogsView from '../LogsView.vue'

const { recent } = vi.hoisted(() => ({ recent: vi.fn<(query: LogQuery) => Promise<LogPage>>() }))
vi.mock('@/services/logsApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/services/logsApi')>()),
  logsApi: { recent },
}))

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

const page: LogPage = {
  entries: [
    {
      time: '2026-10-16T10:05:00+02:00',
      level: 'Error',
      source: 'worker',
      message: 'Failed to persist telemetry',
      exception: 'System.InvalidOperationException: boom',
    },
    {
      time: '2026-10-16T10:00:00+02:00',
      level: 'Information',
      source: 'api',
      message: 'HTTP GET /api/vehicles responded 200',
      exception: null,
    },
  ],
  sources: [
    { name: 'api', available: true },
    { name: 'worker', available: true },
  ],
}

async function render(result: LogPage = page) {
  recent.mockResolvedValue(result)
  const wrapper = mount(LogsView, {
    global: { plugins: [i18n], stubs: { FontAwesomeIcon: true } },
  })
  await flushPromises()
  return wrapper
}

describe('LogsView', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    recent.mockReset()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('shows the entries newest first, with their level, source and any exception', async () => {
    const wrapper = await render()

    expect(recent).toHaveBeenCalledWith({ source: 'all', level: 'Information', search: '' })
    const entries = wrapper.findAll('.logs-entry')
    expect(entries).toHaveLength(2)
    expect(entries[0]!.find('.badge').classes()).toContain('badge-danger')
    expect(entries[0]!.text()).toContain(en.logs.sources.worker)
    expect(entries[0]!.find('details pre').text()).toBe('System.InvalidOperationException: boom')
    expect(entries[1]!.find('.badge').classes()).toContain('badge-info')
    expect(entries[1]!.find('details').exists()).toBe(false)
  })

  it('reads again for another source or level', async () => {
    const wrapper = await render()

    await wrapper.find('select').setValue('Warning')
    await flushPromises()

    expect(recent).toHaveBeenLastCalledWith({ source: 'all', level: 'Warning', search: '' })
  })

  it("says the Worker's files are out of sight when the API cannot read them", async () => {
    const wrapper = await render({
      entries: [],
      sources: [
        { name: 'api', available: true },
        { name: 'worker', available: false },
      ],
    })

    expect(wrapper.text()).toContain(en.logs.workerMissing)
    expect(wrapper.text()).toContain(en.logs.empty)
  })

  it('waits for a pause in typing before searching', async () => {
    vi.useFakeTimers()
    const wrapper = await render()
    recent.mockClear()

    await wrapper.find('input[type=search]').setValue('mqtt')
    expect(recent).not.toHaveBeenCalled()
    await vi.advanceTimersByTimeAsync(300)

    expect(recent).toHaveBeenCalledWith({ source: 'all', level: 'Information', search: 'mqtt' })
  })
})
