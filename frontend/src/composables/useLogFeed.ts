import { computed, ref, shallowRef } from 'vue'
import { logsApi, type LogEntry, type LogPage, type LogQuery } from '@/services/logsApi'
import { useAsyncRequest } from './useAsyncRequest'

/** What the log viewer narrows the entries down by. */
export type LogFilters = Pick<LogQuery, 'source' | 'level' | 'search'>

/**
 * The log viewer's entries, a page at a time: the newest page first, the page before the oldest
 * entry shown as the list scrolls, and in live mode only what was written since the last read,
 * on top. An older page asked for before the list was replaced is dropped, so the list never
 * mixes two filters or leaves a gap.
 */
export function useLogFeed(filters: () => LogFilters, pageSize: number) {
  // Shallow: entries are replaced, never changed, and the list grows into the thousands.
  const entries = shallowRef<LogEntry[]>([])
  const sources = shallowRef<LogPage['sources']>([])
  const loaded = ref(false)
  const older = ref<string | null>(null)
  let newer: string | null = null
  // Bumped whenever the list is replaced, so an older page asked for before that is dropped.
  let generation = 0

  // The newest page and live reads share one request, so the newest of them wins; older pages
  // have their own, to load while live mode keeps reading.
  const newest = useAsyncRequest()
  const previous = useAsyncRequest()

  const query = (cursor: Pick<LogQuery, 'before' | 'after'> = {}): LogQuery => ({
    ...filters(),
    limit: pageSize,
    ...cursor,
  })

  function replace(page: LogPage) {
    generation++
    entries.value = page.entries
    sources.value = page.sources
    older.value = page.older
    newer = page.newer
    // The new list starts without the old one's failure to load more.
    previous.error.value = null
    loaded.value = true
  }

  /** The newest page, in place of the list. */
  function reload() {
    generation++
    return newest.run(() => logsApi.page(query()), replace)
  }

  /** What was written since the last read, on top: the newest page while there is no list yet. */
  function readNewer() {
    if (newest.loading.value) return Promise.resolve(false)
    if (newer === null) return reload()
    const after = newer
    return newest.run(
      () => logsApi.page(query({ after })),
      (page) => {
        if (page.reset) {
          replace(page)
          return
        }
        sources.value = page.sources
        newer = page.newer
        if (page.entries.length > 0) entries.value = [...page.entries, ...entries.value]
      },
    )
  }

  /** The page before the oldest entry shown, at the end. Not by itself again once that failed. */
  function readOlder() {
    return previous.error.value ? Promise.resolve(false) : retryOlder()
  }

  /** The older page again, after it failed. */
  function retryOlder() {
    const before = older.value
    if (before === null || previous.loading.value) return Promise.resolve(false)
    const since = generation
    return previous.run(
      () => logsApi.page(query({ before })),
      (page) => {
        if (since !== generation) return
        older.value = page.older
        if (page.entries.length > 0) entries.value = [...entries.value, ...page.entries]
      },
    )
  }

  return {
    entries,
    sources,
    loaded,
    hasOlder: computed(() => older.value !== null),
    loading: newest.loading,
    error: newest.error,
    loadingOlder: previous.loading,
    olderError: previous.error,
    reload,
    readNewer,
    readOlder,
    retryOlder,
  }
}
