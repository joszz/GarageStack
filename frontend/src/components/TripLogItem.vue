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

const startTime = computed(() => formatTime(props.entry.startedAt))
const endTime = computed(() => formatTime(props.entry.endedAt))

const durationLabel = computed(() =>
  formatTripDuration(props.entry.startedAt, props.entry.endedAt, t),
)

const distanceLabel = computed(() => units.value.format('distance', loggedDistanceKm(props.entry)))
const fromOdometer = computed(() => odometerDistanceKm(props.entry) !== null)
const purposeKey = computed(() => props.entry.purpose ?? 'unclassified')

// The icon's name, with how the purpose was set when it was set automatically.
const purposeTitle = computed(() => {
  const purpose = t(`tripLog.purpose.${purposeKey.value}`)
  return props.entry.purposeIsAutomatic ? `${purpose} (${t('tripLog.places.automatic')})` : purpose
})
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
      <!-- The purpose at a glance; the detail spells it out. -->
      <span
        class="trip-log-item__icon"
        :class="PURPOSE_BADGE_CLASSES[purposeKey]"
        role="img"
        :title="purposeTitle"
        :aria-label="purposeTitle"
      >
        <font-awesome-icon :icon="PURPOSE_ICONS[purposeKey]" />
        <span v-if="entry.purposeIsAutomatic" class="trip-log-item__auto">
          <font-awesome-icon icon="wand-magic-sparkles" />
        </span>
      </span>

      <span class="trip-log-item__when">
        <span class="trip-log-item__date">{{ dateLabel }}</span>
        <span class="trip-log-item__time text-xs text-muted">
          {{ startTime }}<span class="trip-log-item__end-time"> - {{ endTime }}</span>
        </span>
        <span class="trip-log-item__duration text-xs">{{ durationLabel }}</span>
        <font-awesome-icon
          v-if="entry.isEdited"
          icon="pen-to-square"
          class="trip-log-item__mark trip-log-item__edited"
          :title="t('tripLog.editedHint')"
          role="img"
          :aria-label="t('tripLog.edited')"
        />
        <font-awesome-icon
          v-if="entry.notes"
          icon="message"
          class="trip-log-item__mark trip-log-item__note"
          :title="entry.notes"
          role="img"
          :aria-label="entry.notes"
        />
      </span>

      <span
        class="trip-log-item__km"
        :title="fromOdometer ? t('tripLog.distanceFromOdometer') : t('tripLog.distanceFromGps')"
        >{{ distanceLabel }}</span
      >

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

      <font-awesome-icon icon="chevron-right" class="trip-log-item__chevron" aria-hidden="true" />
    </button>
  </li>
</template>
