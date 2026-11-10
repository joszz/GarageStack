<script setup lang="ts">
import '@/assets/multiselect.css'
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import Multiselect from '@vueform/multiselect'
import { useDawarich } from '@/composables/useDawarich'
import { formatDateTime } from '@/utils/format'
import SettingsSection from './SettingsSection.vue'

const props = defineProps<{ vin: string }>()
const { t } = useI18n()
const { status, trackers, selected, saved, queued, loading, error, dirty, load, save, backfill } =
  useDawarich(props.vin)
const from = ref('')
const today = new Date().toISOString().slice(0, 10)
const state = computed(() => trackers.value?.error ?? status.value?.status)
const options = computed(() => {
  const available = trackers.value?.available ?? []
  const ids = new Set([
    ...available.map((tracker) => tracker.trackerId),
    ...(trackers.value?.linked.map((tracker) => tracker.trackerId) ?? []),
  ])
  return [...ids].map((id) => {
    const lastSeen = available.find((tracker) => tracker.trackerId === id)?.lastSeenAt
    return {
      value: id,
      label: lastSeen ? `${id} (${formatDateTime(lastSeen)})` : id,
    }
  })
})
</script>

<template>
  <SettingsSection v-if="status?.configured || error" :title="t('dawarich.title')">
    <div class="form-stack">
      <p class="settings-toggle__desc">{{ t('dawarich.privacy') }}</p>
      <p v-if="state" class="text-sm" role="status">{{ t(`dawarich.status.${state}`) }}</p>
      <p v-if="error" class="text-danger text-sm" role="alert">{{ error }}</p>
      <button
        type="button"
        class="btn btn-outline-secondary btn-sm"
        :disabled="loading"
        @click="load"
      >
        <font-awesome-icon icon="rotate-left" /> {{ t('dawarich.refresh') }}
      </button>
      <form v-if="trackers" class="form-stack" @submit.prevent="save">
        <label id="phone-trackers-label" class="settings-toggle__label">{{
          t('dawarich.trackers')
        }}</label>
        <Multiselect
          v-model="selected"
          :options="options"
          mode="tags"
          :max="10"
          :searchable="true"
          :close-on-select="false"
          :disabled="loading"
          :aria="{ 'aria-labelledby': 'phone-trackers-label' }"
          :placeholder="t('dawarich.choose')"
          :no-options-text="t('dawarich.none')"
          :no-results-text="t('dawarich.noResults')"
          append-to="body"
        />
        <p class="settings-toggle__desc">{{ t('dawarich.linkHint') }}</p>
        <button type="submit" class="btn btn-primary btn-sm" :disabled="loading || !dirty">
          {{ t('dawarich.save') }}
        </button>
        <p v-if="saved && !dirty" class="text-sm" role="status">{{ t('dawarich.saved') }}</p>
      </form>
      <form v-if="trackers?.linked.length" class="form-stack" @submit.prevent="backfill(from)">
        <label for="phone-backfill-from" class="settings-toggle__label">{{
          t('dawarich.from')
        }}</label>
        <input
          id="phone-backfill-from"
          v-model="from"
          type="date"
          class="form-control"
          min="2000-01-01"
          :max="today"
          required
          :disabled="loading"
        />
        <p class="settings-toggle__desc">{{ t('dawarich.backfillHint') }}</p>
        <button
          type="submit"
          class="btn btn-outline-secondary btn-sm"
          :disabled="loading || dirty || !from"
        >
          {{ t('dawarich.backfill') }}
        </button>
        <p v-if="queued" class="text-sm" role="status">{{ t('dawarich.queued') }}</p>
      </form>
    </div>
  </SettingsSection>
</template>
