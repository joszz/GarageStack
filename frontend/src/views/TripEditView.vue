<script setup lang="ts">
import '@/assets/map.css'
import '@/assets/tripEdit.css'
import { computed, onMounted, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { useVehicleStore } from '@/stores/vehicle'
import { useTripEditor } from '@/composables/useTripEditor'
import { useReverseGeocode } from '@/composables/useReverseGeocode'
import { useErrorMessage } from '@/composables/useErrorMessage'
import { useLeaveGuard } from '@/composables/useLeaveGuard'
import ViewHeader from '@/components/ViewHeader.vue'
import TripEditMap from '@/components/TripEditMap.vue'
import TripEditPanel from '@/components/TripEditPanel.vue'
import { DAY_MONTH, formatDate, formatTime } from '@/utils/format'
import { endLabel } from '@/utils/tripLog'

// The route editor for one saved trip: a page of its own rather than a dialog, since panning a
// map inside a scrolling sheet fights the scroll on a phone. Wiring only: the editing is
// useTripEditor's, the drawing TripEditMap's and the controls TripEditPanel's.
const { t } = useI18n()
const route = useRoute()
const router = useRouter()
const vehicleStore = useVehicleStore()
const errorMessage = useErrorMessage()
const { requestPlaces, placeFor } = useReverseGeocode()

const vin = computed(() => vehicleStore.activeVin)
const tripId = computed(() => Number(route.params.id))
const editing = useTripEditor(vin, tripId)
const {
  editor,
  points,
  recorded,
  mode,
  selected,
  loading,
  saving,
  error,
  dirty,
  isRecorded,
  canUndo,
  canRemove,
  canAdd,
  canUsePreviousStop,
  canUseNextStop,
  suggestion,
  distanceBeforeKm,
  distanceAfterKm,
} = editing

// Cancel, the menu, the back button and a reload all ask first while there is something unsaved.
// A save that went through leaves nothing unsaved, so leaving after it asks nothing.
useLeaveGuard(dirty, () => t('tripEdit.leaveConfirm'))

// The trip loads once the car is known: at once when it already is, or once the vehicles are in.
onMounted(() => void vehicleStore.fetchVehicles())
watch([vin, tripId], () => void editing.load(), { immediate: true })

const first = computed(() => points.value[0])
const last = computed(() => points.value[points.value.length - 1])

// Addresses for the ends as they move; the lookups are cached, so moving back costs nothing.
watch([first, last], ([from, to]) => {
  const ends = [from, to].filter((p) => p !== undefined)
  requestPlaces(
    ends.map((p) => ({ lat: p.latitude, lng: p.longitude })),
    'address',
  )
})

function endText(point: (typeof points.value)[number] | undefined): string {
  if (!point) return ''
  return endLabel(
    placeFor(point.latitude, point.longitude, 'address'),
    point.latitude,
    point.longitude,
  )
}

const caption = computed(() => {
  const from = first.value
  const to = last.value
  if (!from || !to) return undefined
  const day = formatDate(from.recordedAt, { weekday: 'short', ...DAY_MONTH })
  return `${day}, ${formatTime(from.recordedAt)} - ${formatTime(to.recordedAt)}`
})

const errorText = computed(() => errorMessage(error.value))

// Back where the editor was opened from (the trip log or the map), or to the trip log when it was
// opened directly.
function leave() {
  if (window.history.state?.back) router.back()
  else void router.push({ name: 'tripLog' })
}

async function save() {
  if (await editing.save()) leave()
}
</script>

<template>
  <div class="view-container view-container--map">
    <ViewHeader :title="t('tripEdit.title')" :caption="caption" />

    <div v-if="loading && !editor" class="trip-edit-layout" role="status">
      <div class="map-wrapper skeleton" :aria-label="t('tripEdit.loading')" />
    </div>

    <div v-else-if="!editor" class="empty-state text-sm">
      <p>{{ errorText ?? t('tripEdit.notFound') }}</p>
      <RouterLink class="btn btn-outline-secondary btn-sm" :to="{ name: 'tripLog' }">
        {{ t('tripEdit.backToLog') }}
      </RouterLink>
    </div>

    <div v-else class="trip-edit-layout">
      <TripEditMap
        :points="points"
        :recorded="recorded"
        :mode="mode"
        :selected="selected"
        :previous-stop="editor.previousStop"
        :next-stop="editor.nextStop"
        @place="editing.placeAt"
        @move-start="editing.moveStart"
        @move-end="editing.moveEnd"
        @select-point="editing.selectPoint"
      />
      <TripEditPanel
        v-model:mode="mode"
        :distance-before-km="distanceBeforeKm"
        :distance-after-km="distanceAfterKm"
        :start-label="endText(first)"
        :end-label="endText(last)"
        :suggestion="suggestion"
        :can-use-previous-stop="canUsePreviousStop"
        :can-use-next-stop="canUseNextStop"
        :has-previous-stop="editor.previousStop !== null"
        :has-next-stop="editor.nextStop !== null"
        :can-remove="canRemove"
        :can-add="canAdd"
        :can-undo="canUndo"
        :can-reset="!isRecorded"
        :dirty="dirty"
        :saving="saving"
        :error-text="errorText"
        @use-previous-stop="editing.usePreviousStop"
        @use-next-stop="editing.useNextStop"
        @remove-point="editing.removeSelected"
        @undo="editing.undo"
        @reset="editing.resetToRecorded"
        @save="save"
        @cancel="leave"
      />
    </div>
  </div>
</template>
