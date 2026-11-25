<script setup lang="ts">
import { computed, ref, toRef, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import {
  TRIP_NOTES_MAX_LENGTH,
  TRIP_PURPOSES,
  type ElevationReading,
  type TripLogEntry,
  type TripPurpose,
} from '@/services/tripLogApi'
import type { CommutePlace } from '@/services/commutePlacesApi'
import type { TripPoint } from '@/services/vehicleApi'
import { formatDate, formatTime } from '@/utils/format'
import { formatTripDuration } from '@/utils/tripRows'
import {
  loggedDistanceKm,
  odometerDistanceKm,
  PURPOSE_BADGE_CLASSES,
  PURPOSE_ICONS,
  tripFuel,
} from '@/utils/tripLog'
import { useVehicleStore } from '@/stores/vehicle'
import { COMMUTE_PLACE_ICONS } from '@/utils/commutePlaces'
import type { ChipOption } from '@/utils/chips'
import type { DetailFigure } from '@/utils/detailFigures'
import { useUnits } from '@/composables/useUnits'
import { useTripEnds } from '@/composables/useTripEnds'
import { useTripSpeedChart } from '@/composables/useTripSpeedChart'
import { useTripElevationChart } from '@/composables/useTripElevationChart'
import ChipGroup from './ChipGroup.vue'
import StatsChartCard from './StatsChartCard.vue'
import TripRouteMap from './TripRouteMap.vue'
import DetailFigureList from './DetailFigureList.vue'
import { useConnection } from '@/composables/useConnection'
import { tripGpx, tripGpxFileName } from '@/utils/gpx'
import { downloadGpx } from '@/utils/download'

const props = defineProps<{
  entry: TripLogEntry
  /** The route's fixes, for the map and the speed chart; null while loading or unavailable. */
  routePoints: readonly TripPoint[] | null
  routeLoading: boolean
  /** Why the route could not be loaded, already translated. */
  routeError: string | null
  /** False when place names are switched off: the ends then read as coordinates. */
  placesShown: boolean
  /** True while addresses are being looked up, so a missing one shows a placeholder. */
  placesResolving: boolean
  saving: boolean
  /** Home and work, to mark the ends that are at one of them. */
  commutePlaces: readonly CommutePlace[]
  /** A saved trip follows this one, so it can be joined with it. */
  canJoinNext: boolean
  /** The heights the car reported during the trip; empty when it reported none. */
  elevation?: readonly ElevationReading[]
}>()

const emit = defineEmits<{
  (e: 'save', purpose: TripPurpose | null, notes: string | null): void
  /** One of the trip's ends, to set as home or work. */
  (e: 'pickPlace', latitude: number, longitude: number): void
  (e: 'setHidden', hidden: boolean): void
  /** Asks to join with the trip after it; the page confirms first. */
  (e: 'joinNext'): void
  /** Asks to undo the join that made this trip; the page confirms, since later changes are lost. */
  (e: 'unjoin'): void
}>()

const { t } = useI18n()
// Without the server a trip is only read: its purpose and notes show but cannot change, and the
// actions that change it are not offered.
const connection = useConnection()
const readOnly = computed(() => props.saving || !connection.reachable)
const units = useUnits()

const { from, to } = useTripEnds({
  entry: toRef(props, 'entry'),
  placesShown: toRef(props, 'placesShown'),
  placesResolving: toRef(props, 'placesResolving'),
  commutePlaces: toRef(props, 'commutePlaces'),
})

const dateLabel = computed(() =>
  formatDate(props.entry.startedAt, {
    weekday: 'long',
    day: 'numeric',
    month: 'long',
    year: 'numeric',
  }),
)

const timeLabel = computed(
  () => `${formatTime(props.entry.startedAt)} - ${formatTime(props.entry.endedAt)}`,
)

const durationLabel = computed(() =>
  formatTripDuration(props.entry.startedAt, props.entry.endedAt, t),
)

const purposeKey = computed(() => props.entry.purpose ?? 'unclassified')

// The odometer at either end, which the tax log records; the list leaves it to this detail.
const odometerLabel = computed(() => {
  const start = units.value.measure('distance', props.entry.odometerStartKm)
  const finish = units.value.measure('distance', props.entry.odometerEndKm)
  return start && finish ? `${start.value} - ${finish.value}` : null
})

const vehicleStore = useVehicleStore()
// Only a plain hybrid's counter holds fuel; on another car the trip shows no fuel at all.
const fuel = computed(() => tripFuel(props.entry, vehicleStore.effectiveVehicleType))

const fuelFigures = computed((): DetailFigure[] =>
  fuel.value
    ? [
        {
          key: 'fuel',
          icon: 'gas-pump',
          label: t('tripLog.detail.fuelUsed'),
          measure: units.value.measure('volume', fuel.value.litres, { decimals: 2 }),
          title: t('tripLog.detail.fuelUsedHint'),
        },
        {
          key: 'consumption',
          icon: 'leaf',
          label: t('tripLog.detail.consumption'),
          measure: units.value.measure('fuelConsumption', fuel.value.litresPer100Km),
          title: t('tripLog.detail.consumptionHint'),
        },
      ]
    : [],
)

const figures = computed((): DetailFigure[] => [
  {
    key: 'distance',
    icon: 'road',
    label: t('tripLog.detail.distance'),
    measure: units.value.measure('distance', loggedDistanceKm(props.entry)),
    title:
      odometerDistanceKm(props.entry) !== null
        ? t('tripLog.distanceFromOdometer')
        : t('tripLog.distanceFromGps'),
    note: odometerLabel.value
      ? { icon: 'gauge', text: odometerLabel.value, title: t('tripLog.odometer') }
      : null,
  },
  {
    key: 'duration',
    icon: 'clock',
    label: t('tripLog.detail.duration'),
    measure: { value: durationLabel.value, unit: '' },
  },
  {
    key: 'avgSpeed',
    icon: 'gauge',
    label: t('tripLog.detail.averageSpeed'),
    measure: units.value.measure('speed', props.entry.avgMovingSpeedKmh, { decimals: 0 }),
    title: t('tripLog.detail.averageSpeedHint'),
  },
  {
    key: 'topSpeed',
    icon: 'gauge-high',
    label: t('tripLog.detail.topSpeed'),
    measure: units.value.measure('speed', props.entry.maxSpeedKmh, { decimals: 0 }),
  },
  ...fuelFigures.value,
])

const speedChart = useTripSpeedChart(toRef(props, 'routePoints'))
const elevationChart = useTripElevationChart(computed(() => props.elevation ?? []))

// The field is edited locally and sent when it loses focus, rather than on every keystroke.
const notesDraft = ref(props.entry.notes ?? '')
watch(
  () => [props.entry.id, props.entry.notes] as const,
  ([, notes]) => {
    notesDraft.value = notes ?? ''
  },
)

function normalizedNotes(): string | null {
  const trimmed = notesDraft.value.trim()
  return trimmed === '' ? null : trimmed
}

// Each purpose's modifier class sets its own chip colour (tripLog.css).
const purposeOptions = computed((): ChipOption<TripPurpose>[] =>
  TRIP_PURPOSES.map((purpose) => ({
    value: purpose,
    label: t(`tripLog.purpose.${purpose}`),
    icon: PURPOSE_ICONS[purpose],
    class: `trip-log-purpose trip-log-purpose--${purpose}`,
  })),
)

// Choosing the purpose a trip already has clears it again, back to unclassified.
function choosePurpose(purpose: TripPurpose | null) {
  emit('save', purpose, props.entry.notes)
}

function saveNotes() {
  const notes = normalizedNotes()
  if (notes === props.entry.notes) return
  emit('save', props.entry.purpose, notes)
}

const notesId = computed(() => `trip-log-notes-${props.entry.id}`)

// The route as the preview draws it, fixes the phone added and points placed by hand included.
// It is built from what the page already holds, so it downloads without the server too.
const canDownloadGpx = computed(() => (props.routePoints?.length ?? 0) > 1)

function downloadRoute() {
  const points = props.routePoints
  if (!points || points.length < 2) return
  const name = t('tripLog.gpx.name', {
    date: formatDate(props.entry.startedAt),
    from: from.value.label ?? '',
    to: to.value.label ?? '',
  })
  downloadGpx(tripGpxFileName(props.entry.startedAt), tripGpx(name, points))
}
</script>

<template>
  <article
    class="list-detail-item trip-log-detail"
    :class="{ 'trip-log-detail--saving': saving, 'trip-log-detail--hidden': entry.isHidden }"
  >
    <header class="list-detail-item__header">
      <h2 class="list-detail-item__title">{{ dateLabel }}</h2>
      <div class="list-detail-item__status">
        <span class="list-detail-item__time text-sm text-muted">{{ timeLabel }}</span>
        <span
          v-if="routePoints?.some((point) => point.source === 'phone')"
          class="trip-log-detail__mark text-xs text-muted"
        >
          <font-awesome-icon icon="route" />{{ t('dawarich.fused') }}
        </span>
        <span
          class="list-detail-item__badge trip-log-detail__badge"
          :class="PURPOSE_BADGE_CLASSES[purposeKey]"
        >
          <font-awesome-icon :icon="PURPOSE_ICONS[purposeKey]" />{{
            t(`tripLog.purpose.${purposeKey}`)
          }}
        </span>
        <span v-if="entry.purposeIsAutomatic" class="trip-log-detail__mark text-xs text-muted">
          <font-awesome-icon icon="wand-magic-sparkles" />{{ t('tripLog.places.automaticHint') }}
        </span>
        <span
          v-if="entry.isEdited"
          class="trip-log-detail__mark trip-log-detail__edited text-xs text-muted"
        >
          <font-awesome-icon icon="pen-to-square" />{{ t('tripLog.editedHint') }}
        </span>
      </div>
    </header>

    <div class="trip-log-detail__ends">
      <template v-for="end in [from, to]" :key="end === from ? 'from' : 'to'">
        <span
          v-if="end.pending"
          class="skeleton skeleton--text skeleton--text-md"
          :aria-label="t('tripLog.resolvingPlaces')"
        />
        <button
          v-else
          type="button"
          class="trip-log-detail__place"
          :class="end === from ? 'trip-log-detail__place--start' : 'trip-log-detail__place--end'"
          :disabled="!connection.reachable"
          :title="t('tripLog.places.useEnd', { place: end.label })"
          :aria-label="t('tripLog.places.useEnd', { place: end.label })"
          @click="emit('pickPlace', end.lat, end.lng)"
        >
          <font-awesome-icon
            v-if="end.commutePlace"
            :icon="COMMUTE_PLACE_ICONS[end.commutePlace.kind]"
            class="trip-log-detail__place-icon"
          />{{ end.label }}
        </button>
      </template>
    </div>

    <TripRouteMap
      :key="entry.id"
      :points="routePoints"
      :loading="routeLoading"
      :start="[entry.startLatitude, entry.startLongitude]"
      :end="[entry.endLatitude, entry.endLongitude]"
      :trip-id="entry.id"
      :started-at="entry.startedAt"
      :link-to-map="!entry.isHidden"
    />
    <p v-if="routeError" class="text-xs text-muted" role="status">
      {{ t('tripLog.detail.routeUnavailable') }}
    </p>

    <DetailFigureList :figures="figures" />

    <StatsChartCard
      v-if="speedChart"
      class="list-detail-item__chart"
      :title="t('tripLog.detail.speedChart')"
      icon="chart-line"
      type="line"
      :data="speedChart.data"
      :options="speedChart.options"
      :show-info="false"
    />

    <StatsChartCard
      v-if="elevationChart"
      class="list-detail-item__chart"
      :title="t('tripLog.detail.elevationChart')"
      icon="mountain"
      type="line"
      :data="elevationChart.data"
      :options="elevationChart.options"
      :show-info="false"
    />

    <section class="trip-log-detail__classify" :aria-label="t('tripLog.purposeLabel')">
      <ChipGroup
        class="trip-log-detail__purpose"
        :options="purposeOptions"
        :model-value="entry.purpose"
        :group-label="t('tripLog.purposeLabel')"
        :disabled="readOnly"
        deselectable
        @update:model-value="choosePurpose"
      />

      <label :for="notesId" class="text-xs text-muted">{{ t('tripLog.notesLabel') }}</label>
      <input
        :id="notesId"
        v-model="notesDraft"
        type="text"
        class="trip-log-detail__notes"
        :maxlength="TRIP_NOTES_MAX_LENGTH"
        :placeholder="t('tripLog.notesPlaceholder')"
        :disabled="readOnly"
        @change="saveNotes"
        @keydown.enter="($event.target as HTMLInputElement).blur()"
      />
    </section>

    <!-- Editing the route and opening it on the map are buttons on the route preview. -->
    <div v-if="connection.reachable || canDownloadGpx" class="trip-log-detail__actions">
      <button
        v-if="canDownloadGpx"
        type="button"
        class="btn btn-outline-secondary btn-sm"
        :title="t('tripLog.gpx.hint')"
        @click="downloadRoute"
      >
        <font-awesome-icon icon="download" />{{ t('tripLog.gpx.download') }}
      </button>
      <button
        v-if="connection.reachable && canJoinNext"
        type="button"
        class="btn btn-outline-secondary btn-sm"
        :disabled="saving"
        :title="t('tripLog.joinNextHint')"
        @click="emit('joinNext')"
      >
        <font-awesome-icon icon="link" />{{ t('tripLog.joinNext') }}
      </button>
      <button
        v-if="connection.reachable && entry.isJoined"
        type="button"
        class="btn btn-outline-secondary btn-sm"
        :disabled="saving"
        :title="t('tripLog.unjoinHint')"
        @click="emit('unjoin')"
      >
        <font-awesome-icon icon="link-slash" />{{ t('tripLog.unjoin') }}
      </button>
      <button
        v-if="connection.reachable"
        type="button"
        class="btn btn-outline-secondary btn-sm trip-log-detail__hide"
        :disabled="saving"
        :title="entry.isHidden ? undefined : t('tripLog.hideHint')"
        @click="emit('setHidden', !entry.isHidden)"
      >
        <font-awesome-icon :icon="entry.isHidden ? 'eye' : 'eye-slash'" />{{
          entry.isHidden ? t('tripLog.show') : t('tripLog.hide')
        }}
      </button>
    </div>
  </article>
</template>
