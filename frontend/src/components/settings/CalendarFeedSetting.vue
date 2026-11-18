<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { calendarApi, calendarFeedUrl, webcalUrl, type CalendarLink } from '@/services/calendarApi'
import { useErrorMessage } from '@/composables/useErrorMessage'
import { useTwoTapConfirm } from '@/composables/useTwoTapConfirm'
import { formatDateTime } from '@/utils/format'
import SettingsToggle from '../SettingsToggle.vue'

// A calendar app subscribes to the APK, maintenance due dates and climate schedules. The link's
// token opens that feed and nothing else, and is shown once, to be copied; after that only whether
// there is one, so it can be replaced or stopped.
const { t, locale } = useI18n()
const errorMessage = useErrorMessage()

const link = ref<CalendarLink | null>(null)
const feedUrl = ref<string | null>(null)
const copied = ref(false)
const error = ref<string | null>(null)
const busy = ref(false)
// Replacing or stopping the link breaks every calendar subscribed to it: the first tap asks.
const { armed, tap } = useTwoTapConfirm<'replace' | 'stop'>()

onMounted(async () => {
  try {
    link.value = await calendarApi.status()
  } catch (e) {
    error.value = errorMessage(e)
  }
})

const usage = computed(() => {
  const current = link.value
  if (!current?.exists || !current.createdAt) return ''
  const made = t('settings.calendarFeed.createdAt', { date: formatDateTime(current.createdAt) })
  const used = current.lastUsedAt
    ? t('settings.calendarFeed.lastUsed', { date: formatDateTime(current.lastUsedAt) })
    : t('settings.calendarFeed.neverUsed')
  return `${made} · ${used}`
})

async function create() {
  if (busy.value || (link.value?.exists && !tap('replace'))) return
  error.value = null
  busy.value = true
  try {
    const result = await calendarApi.create()
    link.value = result.details
    feedUrl.value = calendarFeedUrl(result.token, locale.value)
    copied.value = false
  } catch (e) {
    error.value = errorMessage(e)
  } finally {
    busy.value = false
  }
}

async function stop() {
  if (busy.value || !tap('stop')) return
  error.value = null
  busy.value = true
  try {
    await calendarApi.stop()
    link.value = { exists: false, createdAt: null, lastUsedAt: null }
    feedUrl.value = null
  } catch (e) {
    error.value = errorMessage(e)
  } finally {
    busy.value = false
  }
}

async function copy() {
  if (!feedUrl.value) return
  try {
    await navigator.clipboard.writeText(feedUrl.value)
    copied.value = true
  } catch {
    // No clipboard (plain HTTP, or the browser said no): the field is there to copy by hand.
  }
}
</script>

<template>
  <!-- Stacked: the link and its buttons need the row's whole width. -->
  <SettingsToggle
    class="settings-toggle--stacked"
    :label="t('settings.calendarFeed.title')"
    :desc="t('settings.calendarFeed.desc')"
  >
    <template #control>
      <div v-if="feedUrl" class="form-stack" role="status">
        <p class="text-sm">{{ t('settings.calendarFeed.copyNow') }}</p>
        <div class="api-token-form">
          <input
            class="form-control"
            readonly
            :value="feedUrl"
            :aria-label="t('settings.calendarFeed.link')"
            @focus="($event.target as HTMLInputElement).select()"
          />
          <button type="button" class="btn btn-outline-secondary" @click="copy">
            <font-awesome-icon :icon="copied ? 'check' : 'copy'" />
            {{ copied ? t('settings.calendarFeed.copied') : t('settings.calendarFeed.copy') }}
          </button>
        </div>
        <a class="btn btn-primary calendar-feed__subscribe" :href="webcalUrl(feedUrl)">
          <font-awesome-icon icon="calendar-days" />
          {{ t('settings.calendarFeed.subscribe') }}
        </a>
      </div>

      <p v-else-if="link?.exists" class="settings-toggle__desc">
        {{ t('settings.calendarFeed.active') }} · {{ usage }}
      </p>

      <p v-if="error" class="text-danger text-sm" role="alert">{{ error }}</p>

      <div v-if="link" class="calendar-feed__actions">
        <button
          type="button"
          class="btn"
          :class="
            armed === 'replace'
              ? 'btn-danger'
              : link.exists
                ? 'btn-outline-secondary'
                : 'btn-primary'
          "
          :disabled="busy"
          @click="create"
        >
          <font-awesome-icon :icon="link.exists ? 'rotate' : 'link'" />
          {{
            armed === 'replace'
              ? t('settings.calendarFeed.confirmReplace')
              : link.exists
                ? t('settings.calendarFeed.replace')
                : t('settings.calendarFeed.create')
          }}
        </button>
        <button
          v-if="link.exists"
          type="button"
          class="btn"
          :class="armed === 'stop' ? 'btn-danger' : 'btn-outline-secondary'"
          :disabled="busy"
          @click="stop"
        >
          {{
            armed === 'stop'
              ? t('settings.calendarFeed.confirmStop')
              : t('settings.calendarFeed.stop')
          }}
        </button>
      </div>
    </template>
  </SettingsToggle>
</template>
