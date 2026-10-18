import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import type { LogEntry, LogPage, LogQuery } from '@/services/logsApi'
import LogsView from '../LogsView.vue'

const { page } = vi.hoisted(() => ({ page: vi.fn<(query: LogQuery) => Promise<LogPage>>() }))
vi.mock('@/services/logsApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/services/logsApi')>()),
  logsApi: { page },
}))

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

// jsdom has no IntersectionObserver: this one reports the sentinel in view when a test says so.
const observers: IntersectionObserverCallback[] = []
class FakeIntersectionObserver {
  constructor(callback: IntersectionObserverCallback) {
    observers.push(callback)
  }
  observe() {}
  unobserve() {}
  disconnect() {}
}

async function scrollToTheEnd() {
  for (const callback of observers)
    callback(
      [{ isIntersecting: true } as IntersectionObserverEntry],
      {} as unknown as IntersectionObserver,
    )
  await flushPromises()
}

function entry(id: string, overrides: Partial<LogEntry> = {}): LogEntry {
  return {
    id,
    time: '2026-10-16T10:00:00+02:00',
    level: 'Information',
    source: 'api',
    message: `entry ${id}`,
    exception: null,
    ...overrides,
  }
}

const sources: LogPage['sources'] = [
  { name: 'api', available: true },
  { name: 'worker', available: true },
]

const newest: LogPage = {
  entries: [
    entry('worker-20261016.log:900', {
      time: '2026-10-16T10:05:00+02:00',
      level: 'Error',
      source: 'worker',
      message: 'Failed to persist telemetry',
      exception: 'System.InvalidOperationException: boom',
    }),
    entry('api-20261016.log:400', { message: 'HTTP GET /api/vehicles responded 200' }),
  ],
  sources,
  older: 'api-20261016.log:400,worker-20261016.log:900',
  newer: 'api-20261016.log:500,worker-20261016.log:1000',
  reset: true,
}

async function render(first: LogPage = newest) {
  page.mockResolvedValueOnce(first)
  const wrapper = mount(LogsView, {
    global: { plugins: [i18n], stubs: { FontAwesomeIcon: true } },
  })
  await flushPromises()
  return wrapper
}

const messages = (wrapper: Awaited<ReturnType<typeof render>>) =>
  wrapper.findAll('.logs-entry__message').map((m) => m.text())

describe('LogsView', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    page.mockReset()
    observers.length = 0
    vi.stubGlobal('IntersectionObserver', FakeIntersectionObserver)
  })

  afterEach(() => {
    vi.useRealTimers()
    vi.unstubAllGlobals()
  })

  it("shows the newest page, with each entry's level, source and any exception", async () => {
    const wrapper = await render()

    expect(page).toHaveBeenCalledWith({
      source: 'all',
      level: 'Information',
      search: '',
      limit: 50,
    })
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
    page.mockResolvedValue(newest)

    await wrapper.find('select').setValue('Warning')
    await flushPromises()

    expect(page).toHaveBeenLastCalledWith({
      source: 'all',
      level: 'Warning',
      search: '',
      limit: 50,
    })
  })

  it("says the Worker's files are out of sight when the API cannot read them", async () => {
    const wrapper = await render({
      entries: [],
      sources: [
        { name: 'api', available: true },
        { name: 'worker', available: false },
      ],
      older: null,
      newer: null,
      reset: true,
    })

    expect(wrapper.text()).toContain(en.logs.workerMissing)
    expect(wrapper.text()).toContain(en.logs.empty)
  })

  it('waits for a pause in typing before searching', async () => {
    vi.useFakeTimers()
    const wrapper = await render()
    page.mockClear()
    page.mockResolvedValue(newest)

    await wrapper.find('input[type=search]').setValue('mqtt')
    expect(page).not.toHaveBeenCalled()
    await vi.advanceTimersByTimeAsync(300)

    expect(page).toHaveBeenCalledWith({
      source: 'all',
      level: 'Information',
      search: 'mqtt',
      limit: 50,
    })
  })

  it('loads the older page below as the end of the list comes into view, until there is none', async () => {
    const wrapper = await render()
    page.mockResolvedValueOnce({
      entries: [entry('api-20261016.log:0', { message: 'the oldest' })],
      sources,
      older: null,
      newer: null,
      reset: false,
    })

    await scrollToTheEnd()

    expect(page).toHaveBeenLastCalledWith(expect.objectContaining({ before: newest.older }))
    expect(messages(wrapper)).toEqual([
      'Failed to persist telemetry',
      'HTTP GET /api/vehicles responded 200',
      'the oldest',
    ])
    expect(wrapper.text()).toContain(en.logs.end)

    page.mockClear()
    await scrollToTheEnd()
    expect(page).not.toHaveBeenCalled()
  })

  it('puts what was written since on top in live mode', async () => {
    vi.useFakeTimers()
    const wrapper = await render()
    page.mockResolvedValueOnce({
      entries: [entry('api-20261016.log:500', { message: 'just now' })],
      sources,
      older: null,
      newer: 'api-20261016.log:560,worker-20261016.log:1000',
      reset: false,
    })

    await wrapper.find('button[aria-pressed]').trigger('click')
    await vi.advanceTimersByTimeAsync(10_000)

    expect(page).toHaveBeenLastCalledWith(expect.objectContaining({ after: newest.newer }))
    expect(messages(wrapper)).toEqual([
      'just now',
      'Failed to persist telemetry',
      'HTTP GET /api/vehicles responded 200',
    ])
  })
})
