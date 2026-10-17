<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { LCircle, LMap, LMarker } from '@vue-leaflet/vue-leaflet'
import type { LatLng as LeafletLatLng, LeafletMouseEvent } from 'leaflet'
import DetailModal from './DetailModal.vue'
import ChipGroup from './ChipGroup.vue'
import RangeControl from './RangeControl.vue'
import { useCommutePlacesStore } from '@/stores/commutePlaces'
import {
  COMMUTE_PLACE_KINDS,
  COMMUTE_PLACE_LABEL_MAX_LENGTH,
  COMMUTE_RADIUS,
  type CommutePlace,
  type CommutePlaceChange,
  type CommutePlaceKind,
} from '@/services/commutePlacesApi'
import { COMMUTE_PLACE_ICONS } from '@/utils/commutePlaces'
import { DEFAULT_MAP_CENTER, type LeafletMap } from '@/utils/leaflet'
import type { ChipOption } from '@/utils/chips'
import { useLeafletMap } from '@/composables/useLeafletMap'
import { useBasemap } from '@/composables/useBasemap'
import { useUnits } from '@/composables/useUnits'
import { useErrorMessage } from '@/composables/useErrorMessage'

const props = defineProps<{
  open: boolean
  vin: string | null
  /** A trip end to start a new place at, when opened from the trip log; null opens the list. */
  pick: { latitude: number; longitude: number } | null
  /** Where a new place starts without a trip end to start from: the car's last position. */
  fallbackCenter: [number, number] | null
}>()

const emit = defineEmits<{
  (e: 'close'): void
  /** A place was added, changed or removed, and these trips were re-labelled. */
  (e: 'changed', change: CommutePlaceChange): void
}>()

const { t } = useI18n()
const store = useCommutePlacesStore()
const units = useUnits()
const errorMessage = useErrorMessage()

interface Draft {
  id?: number
  kind: CommutePlaceKind
  label: string
  latitude: number
  longitude: number
  radiusMeters: number
}

// Null shows the list; a draft shows the editor for a new or an existing place.
const draft = ref<Draft | null>(null)
const pendingRemove = ref(false)
const lastChange = ref<CommutePlaceChange | null>(null)
// Fixed while a place is edited: moving the pin should not pan the map out from under the pointer.
const mapCenter = ref<[number, number]>(DEFAULT_MAP_CENTER)

const { mapInstance, bindMapReady } = useLeafletMap()
useBasemap(mapInstance)

function startDraft(next: Draft) {
  draft.value = next
  mapCenter.value = [next.latitude, next.longitude]
  pendingRemove.value = false
}

const hasHome = computed(() => store.places.some((p) => p.kind === 'home'))

// One home per car: only the place that already is home can stay home. A car that has two homes
// from before this rule keeps both editable.
const homeTaken = computed(() => {
  const editing = store.places.find((p) => p.id === draft.value?.id)
  return editing?.kind !== 'home' && hasHome.value
})

// The first place someone sets is usually home; after that, work.
function newDraft(latitude: number, longitude: number, kind?: CommutePlaceKind): Draft {
  return {
    kind: kind ?? (hasHome.value ? 'work' : 'home'),
    label: '',
    latitude,
    longitude,
    radiusMeters: COMMUTE_RADIUS.default,
  }
}

function add(kind: CommutePlaceKind) {
  const [latitude, longitude] = props.fallbackCenter ?? DEFAULT_MAP_CENTER
  startDraft(newDraft(latitude, longitude, kind))
}

function edit(place: CommutePlace) {
  startDraft({ ...place, label: place.label ?? '' })
}

watch(
  () => props.open,
  (open) => {
    if (!open) return
    lastChange.value = null
    if (props.pick) startDraft(newDraft(props.pick.latitude, props.pick.longitude))
    else draft.value = null
  },
)

function moveTo(latLng: LeafletLatLng) {
  if (!draft.value) return
  draft.value.latitude = latLng.lat
  draft.value.longitude = latLng.lng
}

function onMapClick(event: LeafletMouseEvent) {
  moveTo(event.latlng)
}

function onMapReady(map: LeafletMap) {
  bindMapReady(map)
}

const pin = computed<[number, number] | null>(() =>
  draft.value ? [draft.value.latitude, draft.value.longitude] : null,
)

const kindOptions = computed((): ChipOption<CommutePlaceKind>[] =>
  COMMUTE_PLACE_KINDS.map((kind) => ({
    value: kind,
    label: t(`tripLog.places.kind.${kind}`),
    icon: COMMUTE_PLACE_ICONS[kind],
    disabled: kind === 'home' && homeTaken.value,
  })),
)

const title = computed(() => {
  if (!draft.value) return t('tripLog.places.title')
  return draft.value.id === undefined ? t('tripLog.places.addTitle') : t('tripLog.places.editTitle')
})

function placeName(place: CommutePlace): string {
  return place.label ?? t(`tripLog.places.kind.${place.kind}`)
}

function radiusText(meters: number): string {
  return units.value.format('distance', meters / 1000, { decimals: 2 }) ?? ''
}

const changeText = computed(() => {
  const change = lastChange.value
  if (!change) return null
  const parts: string[] = []
  if (change.marked > 0) parts.push(t('tripLog.places.marked', change.marked))
  if (change.cleared > 0) parts.push(t('tripLog.places.cleared', change.cleared))
  return parts.length ? parts.join(' ') : t('tripLog.places.noChange')
})

function done(change: CommutePlaceChange | null) {
  if (!change) return
  lastChange.value = change
  draft.value = null
  pendingRemove.value = false
  emit('changed', change)
}

async function save() {
  if (!props.vin || !draft.value) return
  const { id, kind, label, latitude, longitude, radiusMeters } = draft.value
  const trimmed = label.trim()
  done(
    await store.savePlace(
      props.vin,
      { kind, label: trimmed === '' ? null : trimmed, latitude, longitude, radiusMeters },
      id,
    ),
  )
}

async function remove() {
  const id = draft.value?.id
  if (!props.vin || id === undefined) return
  done(await store.removePlace(props.vin, id))
}

function close() {
  draft.value = null
  pendingRemove.value = false
  emit('close')
}
</script>

<template>
  <DetailModal :open="open" :title="title" wide @close="close">
    <template v-if="!draft">
      <p class="text-muted text-sm commute-places__intro">{{ t('tripLog.places.intro') }}</p>

      <p v-if="changeText" class="text-sm commute-places__change" role="status">
        <font-awesome-icon icon="check" />{{ changeText }}
      </p>

      <p v-if="store.places.length === 0" class="text-sm text-muted">
        {{ t('tripLog.places.empty') }}
      </p>
      <div v-else class="detail-list">
        <button
          v-for="place in store.places"
          :key="place.id"
          type="button"
          class="detail-list__item detail-list__item--link"
          :aria-label="t('tripLog.places.editPlace', { place: placeName(place) })"
          @click="edit(place)"
        >
          <font-awesome-icon
            :icon="COMMUTE_PLACE_ICONS[place.kind]"
            class="detail-list__item-icon"
          />
          <span class="detail-list__item-value">{{ placeName(place) }}</span>
          <span class="detail-list__item-sep">-</span>
          <span class="detail-list__item-label">{{
            t('tripLog.places.within', { distance: radiusText(place.radiusMeters) })
          }}</span>
        </button>
      </div>
    </template>

    <form v-else class="form-stack" @submit.prevent="save">
      <ChipGroup
        v-model="draft.kind"
        :options="kindOptions"
        :group-label="t('tripLog.places.kindLabel')"
      />
      <p v-if="homeTaken" class="text-xs text-muted">{{ t('tripLog.places.homeTaken') }}</p>

      <div class="form-group">
        <label for="commute-place-label">{{ t('tripLog.places.label') }}</label>
        <input
          id="commute-place-label"
          v-model="draft.label"
          type="text"
          class="form-control"
          :maxlength="COMMUTE_PLACE_LABEL_MAX_LENGTH"
          :placeholder="t(`tripLog.places.labelPlaceholder.${draft.kind}`)"
        />
      </div>

      <div class="form-group">
        <span class="text-xs text-muted">{{ t('tripLog.places.mapHint') }}</span>
        <div class="commute-places__map">
          <LMap :zoom="16" :center="mapCenter" @ready="onMapReady" @click="onMapClick">
            <LCircle v-if="pin" :lat-lng="pin" :radius="draft.radiusMeters" />
            <LMarker v-if="pin" :lat-lng="pin" draggable @update:lat-lng="moveTo" />
          </LMap>
        </div>
      </div>

      <RangeControl
        v-model="draft.radiusMeters"
        icon="location-dot"
        :label="t('tripLog.places.radius')"
        :value-text="radiusText(draft.radiusMeters)"
        :min="COMMUTE_RADIUS.min"
        :max="COMMUTE_RADIUS.max"
        :step="COMMUTE_RADIUS.step"
      />
      <p class="text-xs text-muted">{{ t('tripLog.places.radiusHint') }}</p>
    </form>

    <p v-if="store.actionError" class="text-danger text-sm" role="alert">
      {{ errorMessage(store.actionError) }}
    </p>

    <template #footer>
      <template v-if="!draft">
        <button
          v-if="!hasHome"
          type="button"
          class="btn btn-outline-secondary"
          @click="add('home')"
        >
          <font-awesome-icon :icon="COMMUTE_PLACE_ICONS.home" />{{ t('tripLog.places.addHome') }}
        </button>
        <button type="button" class="btn btn-outline-secondary" @click="add('work')">
          <font-awesome-icon :icon="COMMUTE_PLACE_ICONS.work" />{{ t('tripLog.places.addWork') }}
        </button>
      </template>
      <template v-else-if="pendingRemove">
        <span class="text-danger text-sm">{{ t('tripLog.places.removeConfirm') }}</span>
        <button type="button" class="btn btn-outline-secondary" @click="pendingRemove = false">
          {{ t('common.cancel') }}
        </button>
        <button type="button" class="btn btn-danger" :disabled="store.loading" @click="remove">
          {{ t('common.confirm') }}
        </button>
      </template>
      <template v-else>
        <button
          v-if="draft.id !== undefined"
          type="button"
          class="btn btn-outline-secondary"
          @click="pendingRemove = true"
        >
          <font-awesome-icon icon="trash" />{{ t('tripLog.places.remove') }}
        </button>
        <button type="button" class="btn btn-outline-secondary" @click="draft = null">
          {{ t('common.cancel') }}
        </button>
        <button type="button" class="btn btn-primary" :disabled="store.loading" @click="save">
          {{ t('tripLog.places.save') }}
        </button>
      </template>
    </template>
  </DetailModal>
</template>
