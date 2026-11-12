<script setup lang="ts">
import '@/assets/tripLog.css'
import { computed, onMounted, onUnmounted, ref, useId, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useVehicleStore } from '@/stores/vehicle'
import { useTripLogStore } from '@/stores/tripLog'
import { useUiSettingsStore } from '@/stores/settingsUi'
import StatusCard from '@/components/StatusCard.vue'
import TripLogItem from '@/components/TripLogItem.vue'
import TripLogDetail from '@/components/TripLogDetail.vue'
import CommutePlacesModal from '@/components/CommutePlacesModal.vue'
import DetailModal from '@/components/DetailModal.vue'
import { useCommutePlacesStore } from '@/stores/commutePlaces'
import { TRIP_PURPOSES, type TripPurpose } from '@/services/tripLogApi'
import { downloadCsv } from '@/utils/download'
import { formatDate, formatNumber } from '@/utils/format'
import {
  distanceShare,
  PURPOSE_CARD_VARIANTS,
  PURPOSE_ICONS,
  PURPOSE_KEYS,
  type TripLogPeriod,
} from '@/utils/tripLog'
import { tripLogCsv, tripLogFileName } from '@/utils/tripLogCsv'
import { formatDurationMs } from '@/utils/tripRows'
import { WIDE_MEDIA_QUERY } from '@/utils/viewport'
import type { Measure } from '@/utils/units'
import { useUnits } from '@/composables/useUnits'
import { useErrorMessage } from '@/composables/useErrorMessage'
import { useMediaQuery } from '@/composables/useMediaQuery'
import { useTripRoute } from '@/composables/useTripRoute'
import ViewHeader from '@/components/ViewHeader.vue'

const { t } = useI18n()
const vehicleStore = useVehicleStore()
const store = useTripLogStore()
const commutePlaces = useCommutePlacesStore()
const uiSettings = useUiSettingsStore()
const units = useUnits()

// On a phone the purposes and driving figures fold away behind one button, so the first trip is
// on the first screen; wider screens always show them, and the button is not drawn there.
const figuresId = useId()
const figuresOpen = ref(false)
const errorMessage = useErrorMessage()

const vin = computed(() => vehicleStore.activeVin)

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
    label: formatDate(new Date(2000, m, 1), { month: 'long' }),
  })),
)

// Hidden trips stay out of the list unless asked for, as they stay out of the totals and export.
const showHidden = ref(false)
const listed = computed(() => (showHidden.value ? store.entries : store.visibleEntries))

// Newest first on screen, where the trips still to classify are; the export stays in date order.
const newestFirst = computed(() => [...listed.value].reverse())

// Every trip but the newest has a saved trip after it to join with. The newest of a past month
// may too, in the month after; the server says so when there is none.
const newestId = computed(() => store.entries[store.entries.length - 1]?.id)

const placesShown = computed(() => uiSettings.placeNamesEnabled && store.placesAvailable)
const unclassified = computed(() => store.totals.unclassified.trips)
const unclassifiedDistance = computed(
  () => units.value.format('distance', store.totals.unclassified.km) ?? '',
)

// ── Totals ────────────────────────────────────────────────

const allTrips = computed(() => units.value.measure('distance', store.summary.km))

const purposeCards = computed(() =>
  PURPOSE_KEYS.map((key) => {
    const total = store.totals[key]
    const share = distanceShare(total, store.summary.km)
    return {
      key,
      label: t('tripLog.totalLabel', { purpose: t(`tripLog.purpose.${key}`), n: total.trips }),
      value: units.value.measure('distance', total.km)?.value ?? null,
      // Nothing left to classify is nothing to warn about.
      variant: key === 'unclassified' && total.trips === 0 ? undefined : PURPOSE_CARD_VARIANTS[key],
      share,
      note: t('tripLog.shareOfDistance', { percent: formatNumber(share * 100, 0) }),
    }
  }),
)

function measured(m: Measure | null) {
  return { value: m?.value ?? null, unit: m?.unit }
}

const summaryCards = computed(() => {
  const s = store.summary
  return [
    {
      key: 'drivingTime',
      icon: 'clock',
      label: t('tripLog.summary.drivingTime'),
      value: s.trips > 0 ? formatDurationMs(s.drivingMs, t) : null,
      unit: undefined,
      subtitle: undefined,
    },
    {
      key: 'averageTrip',
      icon: 'route',
      label: t('tripLog.summary.averageTrip'),
      ...measured(units.value.measure('distance', s.averageTripKm)),
      subtitle: undefined,
    },
    {
      key: 'longestTrip',
      icon: 'flag-checkered',
      label: t('tripLog.summary.longestTrip'),
      ...measured(units.value.measure('distance', s.longestTripKm)),
      subtitle: undefined,
    },
    {
      key: 'averageSpeed',
      icon: 'gauge',
      label: t('tripLog.summary.averageSpeed'),
      ...measured(units.value.measure('speed', s.averageSpeedKmh, { decimals: 0 })),
      subtitle: t('tripLog.summary.averageSpeedHint'),
    },
  ]
})

// ── The trip shown in detail ──────────────────────────────

// Beside the list on a wide screen, where a trip is always shown: the newest until another is
// picked. In a dialog on a narrower one, once a trip is picked.
const wide = useMediaQuery(WIDE_MEDIA_QUERY)
const pickedId = ref<number | null>(null)

// Looked up among all trips, hidden ones too: a trip just hidden stays in view to show again.
const selected = computed(() => {
  const picked =
    pickedId.value === null ? undefined : store.entries.find((e) => e.id === pickedId.value)
  if (picked) return picked
  return wide.value ? (newestFirst.value[0] ?? null) : null
})

const route = useTripRoute(vin, selected)
const detailInModal = computed(() => !wide.value && selected.value !== null)

function closeDetail() {
  pickedId.value = null
}

// What the detail shows and does, the same beside the list and in the dialog.
const detail = computed(() => {
  const entry = selected.value
  if (!entry) return null
  return {
    props: {
      entry,
      routePoints: route.points.value,
      routeLoading: route.loading.value,
      routeError: route.error.value,
      placesShown: placesShown.value,
      placesResolving: store.placesResolving,
      saving: store.saving.has(entry.id),
      commutePlaces: commutePlaces.places,
      canJoinNext: entry.id !== newestId.value,
    },
    on: {
      save: (purpose: TripPurpose | null, notes: string | null) => save(entry.id, purpose, notes),
      pickPlace: openPlaces,
      setHidden: (hidden: boolean) => setHidden(entry.id, hidden),
      joinNext: () => {
        joiningId.value = entry.id
      },
    },
  }
})

// ── Loading ───────────────────────────────────────────────

function lookUpPlaces() {
  if (vin.value && placesShown.value) void store.resolvePlaces(vin.value, uiSettings.locale)
}

async function load() {
  await vehicleStore.fetchVehicles()
  if (!vin.value) return
  void commutePlaces.fetchPlaces(vin.value)
  await store.fetchLog(vin.value, period.value)
  lookUpPlaces()
}

// Another period or car shows other trips: the newest of those, not one picked before.
function reload() {
  pickedId.value = null
  void load()
}

// ── Home and work ─────────────────────────────────────────

const placesOpen = ref(false)
const pick = ref<{ latitude: number; longitude: number } | null>(null)

const carPosition = computed<[number, number] | null>(() => {
  const status = vehicleStore.currentStatus
  return status?.latitude != null && status.longitude != null
    ? [status.latitude, status.longitude]
    : null
})

function openPlaces(latitude?: number, longitude?: number) {
  pick.value = latitude === undefined || longitude === undefined ? null : { latitude, longitude }
  placesOpen.value = true
}

// A place changed, so trips may have been labelled or unlabelled: show the log as it is now.
async function reloadLog() {
  if (!vin.value) return
  await store.fetchLog(vin.value, period.value)
  lookUpPlaces()
}

onMounted(load)
watch(period, reload)
watch(vin, reload)

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

function setHidden(id: number, hidden: boolean) {
  if (vin.value) void store.setHidden(vin.value, id, hidden)
}

// Joining cannot be undone, so it is asked for in a dialog rather than on the trip's own row.
const joiningId = ref<number | null>(null)
const joinBusy = computed(() => joiningId.value !== null && store.saving.has(joiningId.value))

function cancelJoin() {
  if (!joinBusy.value) joiningId.value = null
}

async function confirmJoin() {
  if (vin.value && joiningId.value !== null) await store.mergeWithNext(vin.value, joiningId.value)
  joiningId.value = null
}

function classifyAll(purpose: TripPurpose) {
  if (vin.value) void store.classifyUnclassified(vin.value, purpose)
}

function exportCsv() {
  const csv = tripLogCsv(store.visibleEntries, {
    t,
    units: units.value,
    placesShown: placesShown.value,
  })
  downloadCsv(tripLogFileName(period.value), csv)
}
</script>

<template>
  <div class="view-container">
    <ViewHeader :title="t('tripLog.title')" :subtitle="t('tripLog.subtitle')">
      <template #actions>
        <button type="button" class="btn btn-outline-secondary btn-sm" @click="openPlaces()">
          <font-awesome-icon icon="house" />{{ t('tripLog.places.button') }}
        </button>
        <button
          type="button"
          class="btn btn-primary btn-sm"
          :disabled="store.visibleEntries.length === 0"
          :title="store.placesResolving ? t('tripLog.exportWhileResolving') : undefined"
          @click="exportCsv"
        >
          <font-awesome-icon icon="file-csv" />{{ t('tripLog.export') }}
        </button>
      </template>
    </ViewHeader>

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
      <label v-if="store.hiddenCount > 0" class="trip-log-hidden-toggle text-sm">
        <input v-model="showHidden" type="checkbox" class="form-check-input" />
        {{ t('tripLog.showHidden', store.hiddenCount) }}
      </label>
    </div>

    <div class="trip-log-figures" :class="{ 'trip-log-figures--open': figuresOpen }">
      <section
        :id="`${figuresId}-totals`"
        class="trip-log-totals"
        :aria-label="t('tripLog.totalsLabel')"
      >
        <StatusCard
          class="trip-log-totals__all"
          icon="car"
          :label="t('tripLog.allTrips', { n: store.summary.trips })"
          :value="allTrips?.value ?? null"
          :unit="allTrips?.unit"
        />
        <StatusCard
          v-for="card in purposeCards"
          :key="card.key"
          :icon="PURPOSE_ICONS[card.key]"
          :label="card.label"
          :value="card.value"
          :unit="units.symbol('distance')"
          :variant="card.variant"
          :note="card.note"
          :share="card.share"
          :share-label="card.note"
        />
      </section>

      <button
        type="button"
        class="btn btn-outline-secondary btn-sm trip-log-figures__toggle"
        :aria-expanded="figuresOpen"
        :aria-controls="`${figuresId}-totals ${figuresId}-summary`"
        @click="figuresOpen = !figuresOpen"
      >
        <font-awesome-icon icon="chevron-down" class="trip-log-figures__chevron" />{{
          figuresOpen ? t('tripLog.fewerFigures') : t('tripLog.moreFigures')
        }}
      </button>

      <section
        :id="`${figuresId}-summary`"
        class="trip-log-summary"
        :aria-label="t('tripLog.summary.label')"
      >
        <StatusCard
          v-for="card in summaryCards"
          :key="card.key"
          :icon="card.icon"
          :label="card.label"
          :value="card.value"
          :unit="card.unit"
          :subtitle="card.subtitle"
        />
      </section>
    </div>

    <div v-if="unclassified > 0 && !store.loading" class="trip-log-bulk">
      <span class="text-sm">
        <font-awesome-icon icon="circle-exclamation" class="trip-log-bulk__icon" />{{
          t(
            'tripLog.classifyAll',
            { n: unclassified, distance: unclassifiedDistance },
            unclassified,
          )
        }}
      </span>
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
      {{ errorMessage(store.actionError) }}
    </p>

    <div v-if="store.loadError" class="empty-state text-danger" role="alert">
      {{ errorMessage(store.loadError) }}
    </div>
    <div v-else-if="!store.loading && listed.length === 0" class="empty-state">
      {{ t('tripLog.empty') }}
    </div>
    <div v-else class="trip-log-layout">
      <ul class="trip-log-list">
        <TripLogItem
          v-for="entry in newestFirst"
          :key="entry.id"
          :entry="entry"
          :places-shown="placesShown"
          :places-resolving="store.placesResolving"
          :saving="store.saving.has(entry.id)"
          :commute-places="commutePlaces.places"
          :selected="selected?.id === entry.id"
          @select="pickedId = entry.id"
        />
      </ul>

      <aside
        v-if="wide && detail"
        class="trip-log-layout__detail"
        :aria-label="t('tripLog.detail.title')"
      >
        <TripLogDetail v-bind="detail.props" v-on="detail.on" />
      </aside>
    </div>

    <DetailModal :open="detailInModal" :title="t('tripLog.detail.title')" wide @close="closeDetail">
      <TripLogDetail v-if="detailInModal && detail" v-bind="detail.props" v-on="detail.on" />
    </DetailModal>

    <DetailModal :open="joiningId !== null" :title="t('tripLog.joinNext')" @close="cancelJoin">
      <p class="text-sm">{{ t('tripLog.joinNextHint') }}</p>
      <template #footer>
        <button
          type="button"
          class="btn btn-outline-secondary"
          :disabled="joinBusy"
          @click="cancelJoin"
        >
          {{ t('common.cancel') }}
        </button>
        <button type="button" class="btn btn-warning" :disabled="joinBusy" @click="confirmJoin">
          <font-awesome-icon icon="link" />{{ t('tripLog.joinNextConfirm') }}
        </button>
      </template>
    </DetailModal>

    <CommutePlacesModal
      :open="placesOpen"
      :vin="vin"
      :pick="pick"
      :fallback-center="carPosition"
      @close="placesOpen = false"
      @changed="reloadLog"
    />
  </div>
</template>
