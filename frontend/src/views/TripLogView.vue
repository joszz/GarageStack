<script setup lang="ts">
import '@/assets/tripLog.css'
import { computed, onMounted, onUnmounted, ref, useId, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useVehicleStore } from '@/stores/vehicle'
import { useTripLogStore } from '@/stores/tripLog'
import { useUiSettingsStore } from '@/stores/settingsUi'
import StatusCard from '@/components/StatusCard.vue'
import CardInfoWrap from '@/components/CardInfoWrap.vue'
import TripLogItem from '@/components/TripLogItem.vue'
import TripLogDetail from '@/components/TripLogDetail.vue'
import CommutePlacesModal from '@/components/CommutePlacesModal.vue'
import DetailModal from '@/components/DetailModal.vue'
import TripLogPeriodFields from '@/components/TripLogPeriodFields.vue'
import TripLogSummaryModal from '@/components/TripLogSummaryModal.vue'
import { useCommutePlacesStore } from '@/stores/commutePlaces'
import { TRIP_PURPOSES, type TripPurpose } from '@/services/tripLogApi'
import { formatNumber } from '@/utils/format'
import {
  currentMonthPeriod,
  distanceShare,
  PURPOSE_CARD_VARIANTS,
  PURPOSE_ICONS,
  PURPOSE_KEYS,
  type TripLogPeriod,
} from '@/utils/tripLog'
import { WIDE_MEDIA_QUERY } from '@/utils/viewport'
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

// On a phone the purposes fold away behind one button, so the first trip is on the first screen;
// wider screens always show them, and the button is not drawn there.
const figuresId = useId()
const figuresOpen = ref(false)
const errorMessage = useErrorMessage()

const vin = computed(() => vehicleStore.activeVin)

const period = ref<TripLogPeriod>(currentMonthPeriod())

// Hidden trips stay out of the list unless asked for, as they stay out of the totals and export.
const showHidden = ref(false)
const listed = computed(() => (showHidden.value ? store.entries : store.visibleEntries))

// Newest first on screen, where the trips still to classify are; the store keeps date order.
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
      title: t(`tripLog.purpose.${key}`),
      description: t(`tripLog.cardDesc.${key}`),
      label: t('tripLog.totalLabel', { purpose: t(`tripLog.purpose.${key}`), n: total.trips }),
      value: units.value.measure('distance', total.km)?.value ?? null,
      // Nothing left to classify is nothing to warn about.
      variant: key === 'unclassified' && total.trips === 0 ? undefined : PURPOSE_CARD_VARIANTS[key],
      share,
      note: t('tripLog.shareOfDistance', { percent: formatNumber(share * 100, 0) }),
    }
  }),
)

// The driving figures (time, average and longest trip, average speed) wait in a dialog behind the
// "All trips" card, which leaves the trips themselves higher up the page.
const summaryOpen = ref(false)

// ── The trip shown in detail ──────────────────────────────

// Beside the list on a desktop, where a trip is always shown: the newest until another is
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

// The places themselves are set under Settings > Vehicle; a trip's end opens the same dialog
// with a new place started there.
const placesOpen = ref(false)
const pick = ref<{ latitude: number; longitude: number } | null>(null)

function openPlaces(latitude: number, longitude: number) {
  pick.value = { latitude, longitude }
  placesOpen.value = true
}

// A place changed, here or in the settings, so trips may have been labelled or unlabelled: show
// the log as it is now.
async function reloadLog() {
  if (!vin.value) return
  await store.fetchLog(vin.value, period.value)
  lookUpPlaces()
}

onMounted(load)
watch(period, reload)
watch(vin, reload)
watch(() => commutePlaces.revision, reloadLog)

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
</script>

<template>
  <div class="view-container">
    <ViewHeader :title="t('tripLog.title')" :subtitle="t('tripLog.subtitle')" />

    <div class="trip-log-filters">
      <TripLogPeriodFields v-model="period" />
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
        <CardInfoWrap
          class="trip-log-totals__all"
          :title="t('tripLog.allTripsTitle')"
          :description="t('tripLog.cardDesc.allTrips')"
        >
          <StatusCard
            icon="car"
            :label="t('tripLog.allTrips', { n: store.summary.trips })"
            :value="allTrips?.value ?? null"
            :unit="allTrips?.unit"
            :subtitle="t('tripLog.summary.open')"
            clickable
            @click="summaryOpen = true"
          />
        </CardInfoWrap>
        <CardInfoWrap
          v-for="card in purposeCards"
          :key="card.key"
          :title="card.title"
          :description="card.description"
        >
          <StatusCard
            :icon="PURPOSE_ICONS[card.key]"
            :label="card.label"
            :value="card.value"
            :unit="units.symbol('distance')"
            :variant="card.variant"
            :note="card.note"
            :share="card.share"
            :share-label="card.note"
          />
        </CardInfoWrap>
      </section>

      <button
        type="button"
        class="btn btn-outline-secondary btn-sm trip-log-figures__toggle"
        :aria-expanded="figuresOpen"
        :aria-controls="`${figuresId}-totals`"
        @click="figuresOpen = !figuresOpen"
      >
        <font-awesome-icon icon="chevron-down" class="trip-log-figures__chevron" />{{
          figuresOpen ? t('tripLog.fewerFigures') : t('tripLog.moreFigures')
        }}
      </button>
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

    <TripLogSummaryModal
      :open="summaryOpen"
      :summary="store.summary"
      @close="summaryOpen = false"
    />

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
      :fallback-center="vehicleStore.currentPosition"
      @close="placesOpen = false"
    />
  </div>
</template>
