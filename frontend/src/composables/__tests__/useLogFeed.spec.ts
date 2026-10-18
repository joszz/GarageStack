import { describe, it, expect, vi, beforeEach } from 'vitest'
import { defineComponent, h } from 'vue'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import type { LogEntry, LogPage, LogQuery } from '@/services/logsApi'
import { useLogFeed, type LogFilters } from '../useLogFeed'

const { page } = vi.hoisted(() => ({ page: vi.fn<(query: LogQuery) => Promise<LogPage>>() }))
vi.mock('@/services/logsApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/services/logsApi')>()),
  logsApi: { page },
}))

function entry(id: string): LogEntry {
  return {
    id,
    time: '2026-10-16T10:00:00+02:00',
    level: 'Information',
    source: 'api',
    message: id,
    exception: null,
  }
}

function answer(ids: string[], cursors: Partial<LogPage> = {}): LogPage {
  return {
    entries: ids.map(entry),
    sources: [{ name: 'api', available: true }],
    older: null,
    newer: null,
    reset: true,
    ...cursors,
  }
}

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((res) => (resolve = res))
  return { promise, resolve }
}

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })
const filters: LogFilters = { source: 'all', level: 'Information', search: '' }

// Inside a component, for the translated error messages.
function setup() {
  let feed!: ReturnType<typeof useLogFeed>
  mount(
    defineComponent({
      setup() {
        feed = useLogFeed(() => filters, 2)
        return () => h('div')
      },
    }),
    { global: { plugins: [i18n] } },
  )
  const ids = () => feed.entries.value.map((e) => e.id)
  return { feed, ids }
}

describe('useLogFeed', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    page.mockReset()
  })

  it('starts over on a live read that found more than a page', async () => {
    const { feed, ids } = setup()
    page.mockResolvedValueOnce(answer(['b', 'a'], { older: 'old-1', newer: 'new-1' }))
    await feed.reload()

    page.mockResolvedValueOnce(answer(['z', 'y'], { older: 'old-2', newer: 'new-2' }))
    await feed.readNewer()
    page.mockResolvedValueOnce(answer(['x'], { reset: false }))
    await feed.readOlder()

    expect(page).toHaveBeenNthCalledWith(2, expect.objectContaining({ after: 'new-1' }))
    expect(page).toHaveBeenLastCalledWith(expect.objectContaining({ before: 'old-2' }))
    expect(ids()).toEqual(['z', 'y', 'x'])
    expect(feed.hasOlder.value).toBe(false)
  })

  it('drops an older page that comes back after the list was replaced', async () => {
    const { feed, ids } = setup()
    page.mockResolvedValueOnce(answer(['b', 'a'], { older: 'old-1' }))
    await feed.reload()

    const late = deferred<LogPage>()
    page.mockReturnValueOnce(late.promise)
    const reading = feed.readOlder()
    page.mockResolvedValueOnce(answer(['warning'], { older: null }))
    await feed.reload()
    late.resolve(answer(['older than a'], { reset: false }))
    await reading

    expect(ids()).toEqual(['warning'])
  })

  it('does not ask for a failed older page again by itself, only on a retry', async () => {
    const { feed, ids } = setup()
    page.mockResolvedValueOnce(answer(['b', 'a'], { older: 'old-1' }))
    await feed.reload()

    page.mockRejectedValueOnce(new Error('offline'))
    await feed.readOlder()
    await feed.readOlder()
    expect(page).toHaveBeenCalledTimes(2)
    expect(feed.olderError.value).not.toBeNull()

    page.mockResolvedValueOnce(answer(['older'], { reset: false }))
    await feed.retryOlder()
    await flushPromises()

    expect(ids()).toEqual(['b', 'a', 'older'])
    expect(feed.olderError.value).toBeNull()
  })
})
