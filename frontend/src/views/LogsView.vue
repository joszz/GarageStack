<script setup lang="ts">
import '@/assets/logs.css'
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import ChipGroup from '@/components/ChipGroup.vue'
import {
  LOG_LEVELS,
  logsApi,
  type LogLevel,
  type LogPage,
  type LogSourceName,
} from '@/services/logsApi'
import { useAsyncRequest } from '@/composables/useAsyncRequest'
import { formatDateTime } from '@/utils/format'
import ViewHeader from '@/components/ViewHeader.vue'
import type { BadgeVariant } from '@/utils/levels'
import StatusBadge from '@/components/StatusBadge.vue'

// The newest entries of the API's and the Worker's log files, to see what happened without a
// shell on the server.
const { t } = useI18n()

const LIVE_INTERVAL_MS = 10_000
const SEARCH_DELAY_MS = 300
// The levels worth starting from: Verbose is never written, and Fatal alone is next to nothing.
const LEVEL_CHOICES = LOG_LEVELS.filter((l) => l !== 'Verbose' && l !== 'Fatal')
const ENTRY_TIME: Intl.DateTimeFormatOptions = {
  day: 'numeric',
  month: 'short',
  hour: '2-digit',
  minute: '2-digit',
  second: '2-digit',
}

const source = ref<LogSourceName | 'all'>('all')
const level = ref<LogLevel>('Information')
const search = ref('')
const live = ref(false)
const page = ref<LogPage | null>(null)
// The filters, the search box and live mode all read again, and may overlap: the newest wins.
const { loading, error, run } = useAsyncRequest()

const sourceOptions = computed(() =>
  (['all', 'api', 'worker'] as const).map((value) => ({
    value,
    label: t(`logs.sources.${value}`),
  })),
)

const workerMissing = computed(
  () => page.value?.sources.some((s) => s.name === 'worker' && !s.available) ?? false,
)

function load() {
  return run(
    () => logsApi.recent({ source: source.value, level: level.value, search: search.value.trim() }),
    (result) => (page.value = result),
  )
}

function levelVariant(entryLevel: LogLevel): BadgeVariant {
  if (entryLevel === 'Error' || entryLevel === 'Fatal') return 'danger'
  if (entryLevel === 'Warning') return 'warning'
  if (entryLevel === 'Information') return 'info'
  return 'secondary'
}

watch([source, level], load)

let searchTimer: ReturnType<typeof setTimeout> | undefined
watch(search, () => {
  clearTimeout(searchTimer)
  searchTimer = setTimeout(load, SEARCH_DELAY_MS)
})

// Live: read again every few seconds, for watching what the services do right now. A tab out
// of view asks nothing, and catches up the moment it shows again.
let liveTimer: ReturnType<typeof setInterval> | undefined
function followLive() {
  clearInterval(liveTimer)
  if (live.value && !document.hidden) liveTimer = setInterval(load, LIVE_INTERVAL_MS)
}
watch(live, followLive)

function onVisibilityChange() {
  if (!live.value) return
  if (!document.hidden) void load()
  followLive()
}

onMounted(() => {
  void load()
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
        <button class="btn btn-primary btn-sm" :disabled="loading" @click="load">
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
    <p v-else-if="page && page.entries.length === 0" class="empty-state">
      {{ t('logs.empty') }}
    </p>

    <ol v-if="page && page.entries.length > 0" class="logs-list">
      <li v-for="(entry, i) in page.entries" :key="`${entry.time}-${i}`" class="logs-entry">
        <div class="logs-entry__meta">
          <StatusBadge :variant="levelVariant(entry.level)">{{
            t(`logs.levels.${entry.level}`)
          }}</StatusBadge>
          <span>{{ t(`logs.sources.${entry.source}`) }}</span>
          <time :datetime="entry.time">{{ formatDateTime(entry.time, ENTRY_TIME) }}</time>
        </div>
        <p class="logs-entry__message">{{ entry.message }}</p>
        <details v-if="entry.exception" class="logs-entry__exception">
          <summary>{{ t('logs.exception') }}</summary>
          <pre>{{ entry.exception }}</pre>
        </details>
      </li>
    </ol>
  </div>
</template>
