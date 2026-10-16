<script setup lang="ts">
import '@/assets/logs.css'
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import ChipGroup from '@/components/ChipGroup.vue'
import { logsApi, type LogLevel, type LogPage, type LogSourceName } from '@/services/logsApi'
import { useErrorMessage } from '@/composables/useErrorMessage'
import { formatDateTime } from '@/utils/format'

// The newest entries of the API's and the Worker's log files, to see what happened without a
// shell on the server.
const { t } = useI18n()
const errorMessage = useErrorMessage()

const LIVE_INTERVAL_MS = 10_000
const SEARCH_DELAY_MS = 300
// The levels worth starting from: Verbose is never written, and Fatal alone is next to nothing.
const LEVEL_CHOICES: LogLevel[] = ['Debug', 'Information', 'Warning', 'Error']
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
const loading = ref(false)
const error = ref<string | null>(null)

const sourceOptions = computed(() =>
  (['all', 'api', 'worker'] as const).map((value) => ({
    value,
    label: t(`logs.sources.${value}`),
  })),
)

const workerMissing = computed(
  () => page.value?.sources.some((s) => s.name === 'worker' && !s.available) ?? false,
)

async function load() {
  loading.value = true
  try {
    page.value = await logsApi.recent({
      source: source.value,
      level: level.value,
      search: search.value.trim(),
    })
    error.value = null
  } catch (e) {
    error.value = errorMessage(e)
  } finally {
    loading.value = false
  }
}

function levelVariant(entryLevel: LogLevel): string {
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

// Live: read again every few seconds, for watching what the services do right now.
let liveTimer: ReturnType<typeof setInterval> | undefined
watch(live, (on) => {
  clearInterval(liveTimer)
  if (on) liveTimer = setInterval(load, LIVE_INTERVAL_MS)
})

onMounted(load)
onUnmounted(() => {
  clearTimeout(searchTimer)
  clearInterval(liveTimer)
})
</script>

<template>
  <div class="view-container">
    <div class="view-header">
      <h1>{{ t('logs.title') }}</h1>
      <div class="view-header__actions">
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
      </div>
    </div>

    <p class="text-muted view-subtitle">{{ t('logs.subtitle') }}</p>

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
          <span class="badge" :class="`badge-${levelVariant(entry.level)}`">{{
            t(`logs.levels.${entry.level}`)
          }}</span>
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
