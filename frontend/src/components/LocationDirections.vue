<script setup lang="ts">
import { computed, ref, useId, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import type { CarLocation } from '@/utils/carLocation'
import {
  appleDirectionsUrl,
  formatCoordinates,
  geoUri,
  googleDirectionsUrl,
  osmUrl,
} from '@/utils/mapLinks'
import { formatDateTime } from '@/utils/format'
import DetailModal from './DetailModal.vue'

const props = defineProps<{ location: CarLocation | null; open: boolean }>()
const emit = defineEmits<{ close: [] }>()
const { t } = useI18n()
const descriptionId = useId()
const copyResult = ref<'idle' | 'copied' | 'failed'>('idle')
let copyGeneration = 0

const destination = computed(() =>
  props.location && !props.location.mayBeMoving ? props.location : null,
)
const coordinates = computed(() =>
  destination.value
    ? formatCoordinates(destination.value.latitude, destination.value.longitude)
    : null,
)
const destinationKey = computed(() =>
  destination.value ? `${destination.value.vehicleId}:${coordinates.value}` : null,
)
const locationDescription = computed(() => {
  const updatedAt = props.location?.vehicleUpdatedAt
  return updatedAt
    ? t('directions.lastKnownUpdated', { time: formatDateTime(updatedAt) })
    : t('directions.lastKnownUnknown')
})

watch([destinationKey, () => props.open], () => {
  copyGeneration += 1
  copyResult.value = 'idle'
})

async function copyCoordinates() {
  const key = destinationKey.value
  const value = coordinates.value
  if (!props.open || !key || !value) return
  const generation = ++copyGeneration
  const isCurrent = () =>
    props.open && generation === copyGeneration && key === destinationKey.value
  copyResult.value = 'idle'

  try {
    if (!navigator.clipboard?.writeText) {
      copyResult.value = 'failed'
      return
    }
    await navigator.clipboard.writeText(value)
    if (isCurrent()) copyResult.value = 'copied'
  } catch {
    if (isCurrent()) copyResult.value = 'failed'
  }
}
</script>

<template>
  <DetailModal :open="open" :title="t('directions.open')" @close="emit('close')">
    <div class="location-directions">
      <p v-if="location" :id="descriptionId" class="location-directions__note">
        {{ locationDescription }}
      </p>
      <p v-if="location?.mayBeMoving" class="location-directions__note" role="status">
        {{ t('directions.moving') }}
      </p>
      <p v-else-if="!location" class="location-directions__note" role="status">
        {{ t('directions.unavailable') }}
      </p>

      <a
        v-if="destination"
        class="btn btn-primary location-directions__primary"
        :href="googleDirectionsUrl(destination.latitude, destination.longitude)"
        target="_blank"
        rel="noopener noreferrer"
        :aria-describedby="descriptionId"
      >
        <font-awesome-icon :icon="['fab', 'google']" aria-hidden="true" />
        {{ t('directions.google') }}
      </a>
      <button v-else type="button" class="btn btn-primary location-directions__primary" disabled>
        <font-awesome-icon :icon="['fab', 'google']" aria-hidden="true" />
        {{ t('directions.google') }}
      </button>

      <template v-if="destination">
        <a
          class="btn btn-outline-secondary location-directions__option"
          :href="appleDirectionsUrl(destination.latitude, destination.longitude)"
          target="_blank"
          rel="noopener noreferrer"
        >
          <font-awesome-icon :icon="['fab', 'apple']" aria-hidden="true" />
          {{ t('directions.apple') }}
        </a>
        <a
          class="btn btn-outline-secondary location-directions__option"
          :href="geoUri(destination.latitude, destination.longitude, t('directions.car'))"
        >
          <font-awesome-icon icon="mobile-screen" aria-hidden="true" />
          {{ t('directions.mapApp') }}
        </a>
        <a
          class="btn btn-outline-secondary location-directions__option"
          :href="osmUrl(destination.latitude, destination.longitude)"
          target="_blank"
          rel="noopener noreferrer"
        >
          <font-awesome-icon icon="map" aria-hidden="true" />
          {{ t('directions.osm') }}
        </a>
        <button
          type="button"
          class="btn btn-outline-secondary location-directions__option"
          @click="copyCoordinates"
        >
          <font-awesome-icon
            :icon="copyResult === 'copied' ? 'check' : 'copy'"
            aria-hidden="true"
          />
          {{ t('directions.copy') }}
        </button>
        <p v-if="copyResult === 'copied'" class="location-directions__note" role="status">
          {{ t('directions.copied') }}
        </p>
        <template v-else-if="copyResult === 'failed'">
          <p class="location-directions__note" role="alert">{{ t('directions.copyFailed') }}</p>
          <input
            class="form-control location-directions__coordinates"
            :value="coordinates"
            :aria-label="t('directions.coordinates')"
            readonly
            @focus="($event.target as HTMLInputElement).select()"
          />
        </template>
        <p class="location-directions__note">{{ t('directions.shareNotice') }}</p>
      </template>
    </div>
  </DetailModal>
</template>

<style scoped>
.location-directions {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  min-width: 0;
}

.location-directions__note {
  margin: 0;
  color: var(--color-text-muted);
  font-size: 0.85rem;
}

.location-directions__primary,
.location-directions__option {
  max-width: 100%;
  text-align: left;
  white-space: normal;
}

.location-directions__coordinates {
  min-width: 0;
  font-size: 0.75rem;
}
</style>
