<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import type { Place } from '@/services/mapApi'
import {
  TRIP_NOTES_MAX_LENGTH,
  TRIP_PURPOSES,
  type TripLogEntry,
  type TripPurpose,
} from '@/services/tripLogApi'
import { formatDate, formatTime } from '@/utils/format'
import { endLabel, loggedDistanceKm, odometerDistanceKm, PURPOSE_ICONS } from '@/utils/tripLog'
import { useUnits } from '@/composables/useUnits'
import ChipGroup from './ChipGroup.vue'
import type { ChipOption } from '@/utils/chips'

const props = defineProps<{
  entry: TripLogEntry
  /** False when place names are switched off: the ends then read as coordinates. */
  placesShown: boolean
  /** True while addresses are being looked up, so a missing one shows a placeholder. */
  placesResolving: boolean
  saving: boolean
}>()

const emit = defineEmits<{
  (e: 'save', purpose: TripPurpose | null, notes: string | null): void
}>()

const { t } = useI18n()
const units = useUnits()

const dateLabel = computed(() =>
  formatDate(props.entry.startedAt, { weekday: 'short', day: 'numeric', month: 'short' }),
)

const timeLabel = computed(
  () => `${formatTime(props.entry.startedAt)} - ${formatTime(props.entry.endedAt)}`,
)

// A placeholder while the address is on its way, then the address, or the coordinates when
// there is none to be had.
function tripEnd(place: Place | null, lat: number, lng: number) {
  const pending = props.placesShown && place === null && props.placesResolving
  return { pending, label: pending ? null : endLabel(props.placesShown ? place : null, lat, lng) }
}

const from = computed(() =>
  tripEnd(props.entry.startPlace, props.entry.startLatitude, props.entry.startLongitude),
)
const to = computed(() =>
  tripEnd(props.entry.endPlace, props.entry.endLatitude, props.entry.endLongitude),
)

const distanceLabel = computed(() => units.value.format('distance', loggedDistanceKm(props.entry)))
const fromOdometer = computed(() => odometerDistanceKm(props.entry) !== null)
const odometerLabel = computed(() => {
  const start = units.value.measure('distance', props.entry.odometerStartKm)
  const finish = units.value.measure('distance', props.entry.odometerEndKm)
  return start && finish ? `${start.value} - ${finish.value}` : null
})

// The field is edited locally and sent when it loses focus, rather than on every keystroke.
const notesDraft = ref(props.entry.notes ?? '')
watch(
  () => props.entry.notes,
  (notes) => {
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
</script>

<template>
  <li class="trip-log-item" :class="{ 'trip-log-item--saving': saving }">
    <div class="trip-log-item__when">
      <span class="trip-log-item__date">{{ dateLabel }}</span>
      <span class="text-xs text-muted">{{ timeLabel }}</span>
    </div>

    <div class="trip-log-item__route">
      <span
        v-if="from.pending"
        class="skeleton skeleton--text skeleton--text-md"
        :aria-label="t('tripLog.resolvingPlaces')"
      />
      <span v-else class="trip-log-item__place" :title="from.label ?? undefined">{{
        from.label
      }}</span>
      <font-awesome-icon icon="arrow-right" class="trip-log-item__arrow" aria-hidden="true" />
      <span
        v-if="to.pending"
        class="skeleton skeleton--text skeleton--text-md"
        :aria-label="t('tripLog.resolvingPlaces')"
      />
      <span v-else class="trip-log-item__place" :title="to.label ?? undefined">{{ to.label }}</span>
    </div>

    <div class="trip-log-item__distance">
      <span
        class="trip-log-item__km"
        :title="fromOdometer ? t('tripLog.distanceFromOdometer') : t('tripLog.distanceFromGps')"
        >{{ distanceLabel }}</span
      >
      <span v-if="odometerLabel" class="text-xs text-muted" :title="t('tripLog.odometer')">
        <font-awesome-icon icon="gauge" aria-hidden="true" /> {{ odometerLabel }}
      </span>
    </div>

    <div class="trip-log-item__actions">
      <ChipGroup
        class="trip-log-item__purpose"
        :options="purposeOptions"
        :model-value="entry.purpose"
        :group-label="t('tripLog.purposeLabel')"
        :disabled="saving"
        deselectable
        @update:model-value="choosePurpose"
      />

      <input
        v-model="notesDraft"
        type="text"
        class="trip-log-item__notes"
        :maxlength="TRIP_NOTES_MAX_LENGTH"
        :placeholder="t('tripLog.notesPlaceholder')"
        :aria-label="t('tripLog.notesLabel')"
        :disabled="saving"
        @change="saveNotes"
        @keydown.enter="($event.target as HTMLInputElement).blur()"
      />
    </div>
  </li>
</template>
