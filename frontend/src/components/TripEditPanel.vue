<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import ChipGroup from './ChipGroup.vue'
import type { ChipOption } from '@/utils/chips'
import { TRIP_EDIT_MODES, type TripEditMode } from '@/composables/useTripEditor'
import { useUnits } from '@/composables/useUnits'

const props = defineProps<{
  distanceBeforeKm: number
  distanceAfterKm: number
  startLabel: string
  endLabel: string
  /** The trip probably left from the parking spot before it: offer the one-tap fix. */
  suggestion: boolean
  canUsePreviousStop: boolean
  canUseNextStop: boolean
  /** Where the car stood before and after the trip is known, to say why its button is off. */
  hasPreviousStop: boolean
  hasNextStop: boolean
  canRemove: boolean
  /** False once the route holds as many points as the server takes. */
  canAdd: boolean
  canUndo: boolean
  /** The route differs from the one recorded, so it can go back to that. */
  canReset: boolean
  /** There is something to save. */
  dirty: boolean
  saving: boolean
  errorText: string | null
}>()

const emit = defineEmits<{
  (e: 'usePreviousStop'): void
  (e: 'useNextStop'): void
  (e: 'removePoint'): void
  (e: 'undo'): void
  (e: 'reset'): void
  (e: 'save'): void
  (e: 'cancel'): void
}>()

/** What a tap on the map moves. */
const mode = defineModel<TripEditMode>('mode', { required: true })

const { t } = useI18n()
const units = useUnits()

const MODE_ICONS: Record<TripEditMode, string> = {
  start: 'location-dot',
  end: 'flag-checkered',
  points: 'route',
}

const modeOptions = computed((): ChipOption<TripEditMode>[] =>
  TRIP_EDIT_MODES.map((value) => ({
    value,
    label: t(`tripEdit.mode.${value}`),
    icon: MODE_ICONS[value],
  })),
)

// One figure until the route changes; "12 km to 12 km" says nothing.
const distanceText = computed(() =>
  props.dirty
    ? t('tripEdit.distanceChange', {
        before: units.value.format('distance', props.distanceBeforeKm),
        after: units.value.format('distance', props.distanceAfterKm),
      })
    : units.value.format('distance', props.distanceAfterKm),
)

// Why the parking spot button for this mode is off, if it is.
const stopNote = computed((): string | null => {
  if (mode.value === 'start' && !props.canUsePreviousStop) {
    return t(props.hasPreviousStop ? 'tripEdit.atPreviousStop' : 'tripEdit.noPreviousStop')
  }
  if (mode.value === 'end' && !props.canUseNextStop) {
    return t(props.hasNextStop ? 'tripEdit.atNextStop' : 'tripEdit.noNextStop')
  }
  return null
})
</script>

<template>
  <section class="trip-edit-panel" :aria-label="t('tripEdit.panelLabel')">
    <div v-if="suggestion" class="trip-edit-panel__suggestion" role="status">
      <p class="text-sm"><font-awesome-icon icon="circle-info" />{{ t('tripEdit.suggestion') }}</p>
      <button type="button" class="btn btn-primary btn-sm" @click="emit('usePreviousStop')">
        <font-awesome-icon icon="square-parking" />{{ t('tripEdit.usePreviousStop') }}
      </button>
    </div>

    <dl class="trip-edit-panel__facts">
      <div>
        <dt class="text-xs text-muted">{{ t('tripEdit.distance') }}</dt>
        <dd>{{ distanceText }}</dd>
      </div>
      <div class="trip-edit-panel__detail">
        <dt class="text-xs text-muted">{{ t('tripEdit.from') }}</dt>
        <dd>{{ startLabel }}</dd>
      </div>
      <div class="trip-edit-panel__detail">
        <dt class="text-xs text-muted">{{ t('tripEdit.to') }}</dt>
        <dd>{{ endLabel }}</dd>
      </div>
    </dl>
    <p class="text-xs text-muted trip-edit-panel__detail">{{ t('tripEdit.odometerNote') }}</p>

    <ChipGroup v-model="mode" :options="modeOptions" :group-label="t('tripEdit.modeLabel')" />
    <p class="text-xs text-muted">{{ t(`tripEdit.hint.${mode}`) }}</p>
    <p v-if="mode === 'points' && !canAdd" class="text-xs trip-edit-panel__limit" role="status">
      {{ t('tripEdit.pointLimit') }}
    </p>

    <!-- While the banner offers the parking spot, start mode needs no second button for it. -->
    <div v-if="!(mode === 'start' && suggestion)" class="trip-edit-panel__tools">
      <button
        v-if="mode === 'start'"
        type="button"
        class="btn btn-outline-secondary btn-sm"
        :disabled="!canUsePreviousStop"
        @click="emit('usePreviousStop')"
      >
        <font-awesome-icon icon="square-parking" />{{ t('tripEdit.usePreviousStop') }}
      </button>
      <button
        v-else-if="mode === 'end'"
        type="button"
        class="btn btn-outline-secondary btn-sm"
        :disabled="!canUseNextStop"
        @click="emit('useNextStop')"
      >
        <font-awesome-icon icon="square-parking" />{{ t('tripEdit.useNextStop') }}
      </button>
      <button
        v-else
        type="button"
        class="btn btn-outline-secondary btn-sm"
        :disabled="!canRemove"
        @click="emit('removePoint')"
      >
        <font-awesome-icon icon="trash" />{{ t('tripEdit.removePoint') }}
      </button>
      <p v-if="stopNote" class="text-xs text-muted">{{ stopNote }}</p>
    </div>

    <p v-if="errorText" class="text-danger text-sm" role="alert">{{ errorText }}</p>

    <div class="trip-edit-panel__footer">
      <div class="trip-edit-panel__history">
        <button
          type="button"
          class="btn btn-outline-secondary btn-sm"
          :disabled="!canUndo || saving"
          :title="t('tripEdit.undo')"
          :aria-label="t('tripEdit.undo')"
          @click="emit('undo')"
        >
          <font-awesome-icon icon="rotate-left" /><span class="trip-edit-panel__label">{{
            t('tripEdit.undo')
          }}</span>
        </button>
        <button
          type="button"
          class="btn btn-outline-secondary btn-sm"
          :disabled="!canReset || saving"
          :title="t('tripEdit.resetHint')"
          :aria-label="t('tripEdit.reset')"
          @click="emit('reset')"
        >
          <font-awesome-icon icon="eraser" /><span class="trip-edit-panel__label">{{
            t('tripEdit.reset')
          }}</span>
        </button>
      </div>
      <div class="trip-edit-panel__actions">
        <button type="button" class="btn btn-outline-secondary btn-sm" @click="emit('cancel')">
          {{ t('common.cancel') }}
        </button>
        <button
          type="button"
          class="btn btn-primary btn-sm"
          :disabled="!dirty || saving"
          @click="emit('save')"
        >
          <font-awesome-icon :icon="saving ? 'spinner' : 'check'" :spin="saving" />{{
            t('tripEdit.save')
          }}
        </button>
      </div>
    </div>
  </section>
</template>
