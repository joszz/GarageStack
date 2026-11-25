<script setup lang="ts">
import '@/assets/listDetail.css'
import '@/assets/tripLog.css'
import { computed, onMounted, onUnmounted, ref, useId, useTemplateRef, watch } from 'vue'
import { storeToRefs } from 'pinia'
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
import TripLogFilterPanel from '@/components/TripLogFilterPanel.vue'
import TripLogSummaryModal from '@/components/TripLogSummaryModal.vue'
import { useCommutePlacesStore } from '@/stores/commutePlaces'
import { TRIP_PURPOSES, type TripPurpose } from '@/services/tripLogApi'
import { formatNumber } from '@/utils/format'
import {
  currentMonthPeriod,
  distanceShare,
  isSamePeriod,
  periodLabel,
  PURPOSE_CARD_VARIANTS,
  PURPOSE_ICONS,
  PURPOSE_KEYS,
  type TripLogPeriod,
} from '@/utils/tripLog'
import { useUnits } from '@/composables/useUnits'
import { useErrorMessage } from '@/composables/useErrorMessage'
import { useListDetail } from '@/composables/useListDetail'
import { useTripRoute } from '@/composables/useTripRoute'
import { useTripElevation } from '@/composables/useTripElevation'
import { useRoomBelow } from '@/composables/useRoomBelow'
import { useReconnect } from '@/composables/useReconnect'
import { useConnection } from '@/composables/useConnection'
import ViewHeader from '@/components/ViewHeader.vue'
import { localeLanguage } from '@/utils/locale'

const { t } = useI18n()
const vehicleStore = useVehicleStore()
const store = useTripLogStore()
// Classifying needs the server: without it the log is only read.
const connection = useConnection()
const commutePlaces = useCommutePlacesStore()
const uiSettings = useUiSettingsStore()
const units = useUnits()

// On a phone the purposes fold away behind one button, so the first trip is on the first screen;
// wider screens always show them, and the button is not drawn there.
const figuresId = useId()
const figuresOpen = ref(false)
const errorMessage = useErrorMessage()

const vin = computed(() => vehicleStore.activeVin)

// Kept in the settings, as the map and statistics keep theirs, so the page comes back on the
// period it was left on. The current month is stored as none, so a page left there follows the
// calendar into the next month instead of staying on this one.
const period = computed<TripLogPeriod>({
  get: () => uiSettings.tripLogPeriod ?? currentMonthPeriod(),
  set: (value) => {
    uiSettings.tripLogPeriod = isSamePeriod(value, currentMonthPeriod()) ? null : value
  },
})
const caption = computed(() => periodLabel(period.value))

// Hidden trips stay out of the list unless asked for, as they stay out of the totals and export.
const { tripLogShowHidden: showHidden } = storeToRefs(uiSettings)
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
// picked. In a dialog on a narrower one, once a trip is picked. Looked up among all trips,
// hidden ones too: a trip just hidden stays in view to show again.
const {
  wide,
  selected,
  inDialog: detailInModal,
  pick: pickTrip,
  clear: closeDetail,
} = useListDetail(
  (id: number) => store.entries.find((e) => e.id === id),
  () => newestFirst.value[0],
)

const route = useTripRoute(vin, selected)
const elevation = useTripElevation(vin, selected)

// The panel starts below the totals, so it is kept from reaching past the bottom of the window
// until the page scrolls it up; what does not fit scrolls inside it.
useRoomBelow(useTemplateRef<HTMLElement>('detailPanel'))

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
      elevation: elevation.value,
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
      joinNext: () => askJoin(entry.id, 'join'),
      unjoin: () => askJoin(entry.id, 'unjoin'),
    },
  }
})

// ── Loading ───────────────────────────────────────────────

function lookUpPlaces() {
  if (vin.value && placesShown.value)
    void store.resolvePlaces(vin.value, localeLanguage(uiSettings.locale))
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
  closeDetail()
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
// Back from offline: the server's log replaces the saved one.
useReconnect(reloadLog)

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

// Joining two trips, and undoing a join, which loses what changed on the joined trip since, are
// asked for in a dialog rather than done from the trip's own row.
type JoinAction = 'join' | 'unjoin'
const joining = ref<{ id: number; action: JoinAction } | null>(null)
// Kept once the dialog closes, so its text stays while it fades out.
const joinAction = ref<JoinAction>('join')
const joinBusy = computed(() => joining.value !== null && store.saving.has(joining.value.id))

function askJoin(id: number, action: JoinAction) {
  joinAction.value = action
  joining.value = { id, action }
}

const joinDialog = {
  join: {
    title: 'tripLog.joinNext',
    hint: 'tripLog.joinNextHint',
    icon: 'link',
    confirm: 'tripLog.joinNextConfirm',
  },
  unjoin: {
    title: 'tripLog.unjoin',
    hint: 'tripLog.unjoinHint',
    icon: 'link-slash',
    confirm: 'tripLog.unjoinConfirm',
  },
} as const

function cancelJoin() {
  if (!joinBusy.value) joining.value = null
}

async function confirmJoin() {
  const pending = joining.value
  if (vin.value && pending) {
    if (pending.action === 'join') await store.mergeWithNext(vin.value, pending.id)
    else if (await store.unjoin(vin.value, pending.id)) void reloadLog()
  }
  joining.value = null
}

function classifyAll(purpose: TripPurpose) {
  if (vin.value) void store.classifyUnclassified(vin.value, purpose)
}
</script>

<template>
  <div class="view-container">
    <ViewHeader :title="t('tripLog.title')" :caption="caption" :subtitle="t('tripLog.subtitle')">
      <template #actions>
        <TripLogFilterPanel
          v-model:period="period"
          v-model:show-hidden="showHidden"
          :hidden-count="store.hiddenCount"
        />
      </template>
    </ViewHeader>

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

    <div v-if="unclassified > 0 && !store.loading && connection.reachable" class="trip-log-bulk">
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
    <div v-else class="list-detail">
      <ul class="list-detail__list">
        <TripLogItem
          v-for="entry in newestFirst"
          :key="entry.id"
          :entry="entry"
          :places-shown="placesShown"
          :places-resolving="store.placesResolving"
          :saving="store.saving.has(entry.id)"
          :commute-places="commutePlaces.places"
          :selected="selected?.id === entry.id"
          @select="pickTrip(entry.id)"
        />
      </ul>

      <aside
        v-if="wide && detail"
        ref="detailPanel"
        class="list-detail__panel"
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

    <DetailModal
      :open="joining !== null"
      :title="t(joinDialog[joinAction].title)"
      @close="cancelJoin"
    >
      <p class="text-sm">{{ t(joinDialog[joinAction].hint) }}</p>
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
          <font-awesome-icon :icon="joinDialog[joinAction].icon" />{{
            t(joinDialog[joinAction].confirm)
          }}
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
