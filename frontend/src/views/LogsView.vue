<script setup lang="ts">
import '@/assets/logs.css'
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import ChipGroup from '@/components/ChipGroup.vue'
import LogEntryItem from '@/components/LogEntryItem.vue'
import ViewHeader from '@/components/ViewHeader.vue'
import { LOG_LEVELS, type LogLevel, type LogSourceName } from '@/services/logsApi'
import { useLogFeed } from '@/composables/useLogFeed'
import { useScrollSentinel } from '@/composables/useScrollSentinel'
import { scrollParent } from '@/utils/viewport'

// The API's and the Worker's log files, newest first and a page at a time, to see what happened
// without a shell on the server.
const { t } = useI18n()

const PAGE_SIZE = 50
const LIVE_INTERVAL_MS = 10_000
const SEARCH_DELAY_MS = 300
// The levels worth starting from: Verbose is never written, and Fatal alone is next to nothing.
const LEVEL_CHOICES = LOG_LEVELS.filter((l) => l !== 'Verbose' && l !== 'Fatal')

const source = ref<LogSourceName | 'all'>('all')
const level = ref<LogLevel>('Information')
const search = ref('')
const live = ref(false)

const feed = useLogFeed(
  () => ({ source: source.value, level: level.value, search: search.value.trim() }),
  PAGE_SIZE,
)
const { entries, sources, loaded, loading, error, hasOlder, loadingOlder, olderError } = feed

// The next older page starts loading a screen before the end of the list shows.
const { sentinelRef, observe, recheck } = useScrollSentinel(
  () => void feed.readOlder(),
  '0px 0px 100% 0px',
)

const sourceOptions = computed(() =>
  (['all', 'api', 'worker'] as const).map((value) => ({
    value,
    label: t(`logs.sources.${value}`),
  })),
)

const workerMissing = computed(() => sources.value.some((s) => s.name === 'worker' && !s.available))

watch([source, level], feed.reload)

let searchTimer: ReturnType<typeof setTimeout> | undefined
watch(search, () => {
  clearTimeout(searchTimer)
  searchTimer = setTimeout(feed.reload, SEARCH_DELAY_MS)
})

// A list that grew, or an older page that came back empty or failed, leaves the sentinel where it
// was: still in view means another page.
watch([entries, loadingOlder], recheck, { flush: 'post' })

// Live: what was written since, every few seconds, on top of the list. A tab out of view asks
// nothing, and catches up the moment it shows again.
let liveTimer: ReturnType<typeof setInterval> | undefined
function followLive() {
  clearInterval(liveTimer)
  if (live.value && !document.hidden) liveTimer = setInterval(feed.readNewer, LIVE_INTERVAL_MS)
}
watch(live, followLive)

function onVisibilityChange() {
  if (!live.value) return
  if (!document.hidden) void feed.readNewer()
  followLive()
}

onMounted(() => {
  void feed.reload()
  observe(scrollParent(sentinelRef.value))
  document.addEventListener('visibilitychange', onVisibilityChange)
})
onUnmounted(() => {
  clearTimeout(searchTimer)
  clearInterval(liveTimer)
  document.removeEventListener('visibilitychange', onVisibilityChange)
})
</script>

<template>
  <div class="view-container">
    <ViewHeader :title="t('logs.title')" :subtitle="t('logs.subtitle')">
      <template #actions>
        <button
          class="btn btn-sm"
          :class="live ? 'btn-success' : 'btn-outline-secondary'"
          :aria-pressed="live"
          @click="live = !live"
        >
          <font-awesome-icon icon="circle" />{{ t('logs.live') }}
        </button>
        <button class="btn btn-primary btn-sm" :disabled="loading" @click="feed.reload">
          <font-awesome-icon icon="rotate" :spin="loading" />{{ t('logs.refresh') }}
        </button>
      </template>
    </ViewHeader>

    <div class="logs-filters">
      <ChipGroup v-model="source" :options="sourceOptions" :group-label="t('logs.source')" />
      <select v-model="level" class="form-select" :aria-label="t('logs.level')">
        <option v-for="choice in LEVEL_CHOICES" :key="choice" :value="choice">
          {{ t('logs.fromLevel', { level: t(`logs.levels.${choice}`) }) }}
        </option>
      </select>
      <input
        v-model="search"
        class="form-control logs-filters__search"
        type="search"
        :placeholder="t('logs.searchPlaceholder')"
        :aria-label="t('logs.search')"
      />
    </div>

    <p v-if="workerMissing" class="text-muted text-sm">{{ t('logs.workerMissing') }}</p>
    <p v-if="error" class="text-danger text-sm" role="alert">{{ error }}</p>
    <p v-else-if="loaded && entries.length === 0 && !hasOlder" class="empty-state">
      {{ t('logs.empty') }}
    </p>

    <ol v-if="entries.length > 0" class="logs-list">
      <LogEntryItem v-for="entry in entries" :key="entry.id" :entry="entry" />
    </ol>

    <div ref="sentinelRef" aria-hidden="true" />
    <p v-if="loadingOlder" class="logs-more text-muted text-sm">
      <font-awesome-icon icon="spinner" spin />{{ t('logs.loadingOlder') }}
    </p>
    <p v-else-if="olderError" class="logs-more text-danger text-sm" role="alert">
      {{ olderError }}
      <button class="btn btn-outline-secondary btn-sm" @click="feed.retryOlder">
        {{ t('common.retry') }}
      </button>
    </p>
    <p v-else-if="loaded && !hasOlder && entries.length > 0" class="logs-more text-muted text-sm">
      {{ t('logs.end') }}
    </p>
  </div>
</template>
