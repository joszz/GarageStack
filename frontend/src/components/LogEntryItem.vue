<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import type { LogEntry, LogLevel } from '@/services/logsApi'
import { formatDateTime } from '@/utils/format'
import type { BadgeVariant } from '@/utils/levels'
import StatusBadge from './StatusBadge.vue'

// One entry of the log viewer. A component of its own, so a page added to the list renders only
// its own entries, not every one above it again.
defineProps<{ entry: LogEntry }>()

const { t } = useI18n()

const ENTRY_TIME: Intl.DateTimeFormatOptions = {
  day: 'numeric',
  month: 'short',
  hour: '2-digit',
  minute: '2-digit',
  second: '2-digit',
}

function levelVariant(level: LogLevel): BadgeVariant {
  if (level === 'Error' || level === 'Fatal') return 'danger'
  if (level === 'Warning') return 'warning'
  if (level === 'Information') return 'info'
  return 'secondary'
}
</script>

<template>
  <li class="logs-entry">
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
</template>
