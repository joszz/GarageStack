<script setup lang="ts">
import { computed, toRef } from 'vue'
import { useI18n } from 'vue-i18n'
import type { TripLogEntry } from '@/services/tripLogApi'
import type { CommutePlace } from '@/services/commutePlacesApi'
import { DAY_MONTH, formatDate, formatTime } from '@/utils/format'
import { formatTripDuration } from '@/utils/tripRows'
import {
  loggedDistanceKm,
  odometerDistanceKm,
  PURPOSE_BADGE_CLASSES,
  PURPOSE_ICONS,
} from '@/utils/tripLog'
import { COMMUTE_PLACE_ICONS } from '@/utils/commutePlaces'
import { useUnits } from '@/composables/useUnits'
import { useTripEnds } from '@/composables/useTripEnds'

const props = defineProps<{
  entry: TripLogEntry
  /** False when place names are switched off: the ends then read as coordinates. */
  placesShown: boolean
  /** True while addresses are being looked up, so a missing one shows a placeholder. */
  placesResolving: boolean
  saving: boolean
  /** Home and work, to mark the ends that are at one of them. */
  commutePlaces: readonly CommutePlace[]
  /** The trip the detail shows. */
  selected: boolean
}>()

/** Asks for this trip's detail: beside the list, or in a dialog on a narrower screen. */
const emit = defineEmits<{ (e: 'select'): void }>()

const { t } = useI18n()
const units = useUnits()

const { from, to } = useTripEnds({
  entry: toRef(props, 'entry'),
  placesShown: toRef(props, 'placesShown'),
  placesResolving: toRef(props, 'placesResolving'),
  commutePlaces: toRef(props, 'commutePlaces'),
})

const dateLabel = computed(() =>
  formatDate(props.entry.startedAt, { weekday: 'short', ...DAY_MONTH }),
)

const timeLabel = computed(
  () => `${formatTime(props.entry.startedAt)} - ${formatTime(props.entry.endedAt)}`,
)

const durationLabel = computed(() =>
  formatTripDuration(props.entry.startedAt, props.entry.endedAt, t),
)

const distanceLabel = computed(() => units.value.format('distance', loggedDistanceKm(props.entry)))
const fromOdometer = computed(() => odometerDistanceKm(props.entry) !== null)
const odometerLabel = computed(() => {
  const start = units.value.measure('distance', props.entry.odometerStartKm)
  const finish = units.value.measure('distance', props.entry.odometerEndKm)
  return start && finish ? `${start.value} - ${finish.value}` : null
})

const purposeKey = computed(() => props.entry.purpose ?? 'unclassified')
</script>

<template>
  <li
    class="trip-log-item"
    :class="{
      'trip-log-item--saving': saving,
      'trip-log-item--hidden': entry.isHidden,
      'trip-log-item--selected': selected,
    }"
  >
    <button
      type="button"
      class="trip-log-item__select"
      :aria-pressed="selected"
      @click="emit('select')"
    >
      <span class="trip-log-item__when">
        <span class="trip-log-item__date">{{ dateLabel }}</span>
        <span class="trip-log-item__time text-xs text-muted">
          {{ timeLabel }}
          <span class="trip-log-item__duration">{{ durationLabel }}</span>
        </span>
      </span>

      <span class="trip-log-item__route">
        <span
          v-if="from.pending"
          class="skeleton skeleton--text skeleton--text-md"
          :aria-label="t('tripLog.resolvingPlaces')"
        />
        <span v-else class="trip-log-item__place" :title="from.label ?? undefined">
          <font-awesome-icon
            v-if="from.commutePlace"
            :icon="COMMUTE_PLACE_ICONS[from.commutePlace.kind]"
            class="trip-log-item__place-icon"
          />{{ from.label }}
        </span>
        <font-awesome-icon icon="arrow-right" class="trip-log-item__arrow" aria-hidden="true" />
        <span
          v-if="to.pending"
          class="skeleton skeleton--text skeleton--text-md"
          :aria-label="t('tripLog.resolvingPlaces')"
        />
        <span v-else class="trip-log-item__place" :title="to.label ?? undefined">
          <font-awesome-icon
            v-if="to.commutePlace"
            :icon="COMMUTE_PLACE_ICONS[to.commutePlace.kind]"
            class="trip-log-item__place-icon"
          />{{ to.label }}
        </span>
      </span>

      <span class="trip-log-item__distance">
        <span
          class="trip-log-item__km"
          :title="fromOdometer ? t('tripLog.distanceFromOdometer') : t('tripLog.distanceFromGps')"
          >{{ distanceLabel }}</span
        >
        <span v-if="odometerLabel" class="text-xs text-muted" :title="t('tripLog.odometer')">
          <font-awesome-icon icon="gauge" aria-hidden="true" /> {{ odometerLabel }}
        </span>
      </span>

      <span class="trip-log-item__tags">
        <span class="trip-log-item__purpose" :class="PURPOSE_BADGE_CLASSES[purposeKey]">
          <font-awesome-icon :icon="PURPOSE_ICONS[purposeKey]" />{{
            t(`tripLog.purpose.${purposeKey}`)
          }}
        </span>
        <span
          v-if="entry.purposeIsAutomatic"
          class="trip-log-item__auto text-xs text-muted"
          :title="t('tripLog.places.automaticHint')"
        >
          <font-awesome-icon icon="wand-magic-sparkles" />{{ t('tripLog.places.automatic') }}
        </span>
        <span
          v-if="entry.isEdited"
          class="trip-log-item__auto trip-log-item__edited text-xs text-muted"
          :title="t('tripLog.editedHint')"
        >
          <font-awesome-icon icon="pen-to-square" />{{ t('tripLog.edited') }}
        </span>
        <span
          v-if="entry.notes"
          class="trip-log-item__note text-xs text-muted"
          :title="entry.notes"
        >
          {{ entry.notes }}
        </span>
        <font-awesome-icon icon="chevron-right" class="trip-log-item__chevron" aria-hidden="true" />
      </span>
    </button>
  </li>
</template>
