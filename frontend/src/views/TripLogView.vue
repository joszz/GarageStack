<script setup lang="ts">
import '@/assets/tripLog.css'
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useVehicleStore } from '@/stores/vehicle'
import { useTripLogStore } from '@/stores/tripLog'
import { useUiSettingsStore } from '@/stores/settingsUi'
import StatusCard from '@/components/StatusCard.vue'
import TripLogItem from '@/components/TripLogItem.vue'
import { TRIP_PURPOSES, type TripPurpose } from '@/services/tripLogApi'
import { downloadCsv } from '@/utils/download'
import { formatNumber, intlLocale } from '@/utils/format'
import { PURPOSE_ICONS, PURPOSE_KEYS, type TripLogPeriod } from '@/utils/tripLog'
import { tripLogCsv, tripLogFileName } from '@/utils/tripLogCsv'

const { t } = useI18n()
const vehicleStore = useVehicleStore()
const store = useTripLogStore()
const uiSettings = useUiSettingsStore()

const vin = computed(() => vehicleStore.activeVin)
const displayLocale = computed(() => intlLocale(uiSettings.locale))

// The current month to start with: the one a driver is most likely filling in.
const today = new Date()
const year = ref(today.getFullYear())
const month = ref<number | null>(today.getMonth())
const period = computed<TripLogPeriod>(() => ({ year: year.value, month: month.value }))

// Trips are saved from the Worker's first run onwards, which also saves the history before it,
// so a few years back covers any install; there is nothing to page to before that.
const YEARS_OFFERED = 5
const years = computed(() =>
  Array.from({ length: YEARS_OFFERED }, (_, i) => today.getFullYear() - i),
)
const months = computed(() =>
  Array.from({ length: 12 }, (_, m) => ({
    value: m,
    label: new Date(2000, m, 1).toLocaleDateString(displayLocale.value, { month: 'long' }),
  })),
)

// Newest first on screen, where the trips still to classify are; the export stays in date order.
const newestFirst = computed(() => [...store.entries].reverse())

const placesShown = computed(() => uiSettings.placeNamesEnabled && store.placesAvailable)
const unclassified = computed(() => store.totals.unclassified.trips)

function lookUpPlaces() {
  if (vin.value && placesShown.value) void store.resolvePlaces(vin.value, uiSettings.locale)
}

async function load() {
  await vehicleStore.fetchVehicles()
  if (!vin.value) return
  await store.fetchLog(vin.value, period.value)
  lookUpPlaces()
}

onMounted(load)
watch(period, load)
watch(vin, load)

// Switching place names off stops the lookups and shows coordinates; switching them back on asks
// for whatever this page is still missing.
watch(
  () => uiSettings.placeNamesEnabled,
  (on) => {
    if (on) lookUpPlaces()
    else store.stopResolvingPlaces()
  },
)

onUnmounted(() => store.stopResolvingPlaces())

function save(id: number, purpose: TripPurpose | null, notes: string | null) {
  if (vin.value) void store.updateEntry(vin.value, id, purpose, notes)
}

function classifyAll(purpose: TripPurpose) {
  if (vin.value) void store.classifyUnclassified(vin.value, purpose)
}

function exportCsv() {
  const csv = tripLogCsv(store.entries, {
    locale: displayLocale.value,
    t,
    placesShown: placesShown.value,
  })
  downloadCsv(tripLogFileName(period.value), csv)
}
</script>

<template>
  <div class="view-container">
    <div class="view-header">
      <h1>{{ t('tripLog.title') }}</h1>
      <div class="view-header__actions">
        <button
          type="button"
          class="btn btn-primary btn-sm"
          :disabled="store.entries.length === 0"
          :title="store.placesResolving ? t('tripLog.exportWhileResolving') : undefined"
          @click="exportCsv"
        >
          <font-awesome-icon icon="file-csv" />{{ t('tripLog.export') }}
        </button>
      </div>
    </div>

    <p class="text-muted mb-4">{{ t('tripLog.subtitle') }}</p>

    <div class="trip-log-filters">
      <label class="trip-log-filters__field">
        <span class="text-xs text-muted">{{ t('tripLog.year') }}</span>
        <select v-model="year" class="form-select">
          <option v-for="y in years" :key="y" :value="y">{{ y }}</option>
        </select>
      </label>
      <label class="trip-log-filters__field">
        <span class="text-xs text-muted">{{ t('tripLog.month') }}</span>
        <select v-model="month" class="form-select">
          <option :value="null">{{ t('tripLog.wholeYear') }}</option>
          <option v-for="m in months" :key="m.value" :value="m.value">{{ m.label }}</option>
        </select>
      </label>
    </div>

    <section class="trip-log-totals" :aria-label="t('tripLog.totalsLabel')">
      <StatusCard
        v-for="key in PURPOSE_KEYS"
        :key="key"
        :icon="PURPOSE_ICONS[key]"
        :label="
          t('tripLog.totalLabel', {
            purpose: t(`tripLog.purpose.${key}`),
            n: store.totals[key].trips,
          })
        "
        :value="formatNumber(store.totals[key].km)"
        :unit="t('common.km')"
      />
    </section>

    <div v-if="unclassified > 0 && !store.loading" class="trip-log-bulk">
      <span class="text-sm">{{ t('tripLog.classifyAll', unclassified) }}</span>
      <div class="trip-log-bulk__actions">
        <button
          v-for="purpose in TRIP_PURPOSES"
          :key="purpose"
          type="button"
          class="btn btn-outline-secondary btn-sm"
          :disabled="store.saving.size > 0"
          @click="classifyAll(purpose)"
        >
          <font-awesome-icon :icon="PURPOSE_ICONS[purpose]" />{{ t(`tripLog.purpose.${purpose}`) }}
        </button>
      </div>
    </div>

    <p v-if="!placesShown" class="text-xs text-muted">{{ t('tripLog.placesOff') }}</p>
    <p v-if="store.actionError" class="text-sm text-danger" role="alert">
      {{ t('tripLog.saveFailed') }}
    </p>

    <div v-if="store.loadError" class="empty-state text-danger">{{ store.loadError }}</div>
    <div v-else-if="!store.loading && store.entries.length === 0" class="empty-state">
      {{ t('tripLog.empty') }}
    </div>
    <ul v-else class="trip-log-list">
      <TripLogItem
        v-for="entry in newestFirst"
        :key="entry.id"
        :entry="entry"
        :locale="displayLocale"
        :places-shown="placesShown"
        :places-resolving="store.placesResolving"
        :saving="store.saving.has(entry.id)"
        @save="(purpose, notes) => save(entry.id, purpose, notes)"
      />
    </ul>
  </div>
</template>
