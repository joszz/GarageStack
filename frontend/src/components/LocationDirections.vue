<script setup lang="ts">
import { computed, ref, useId, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import type { IconProp } from '@fortawesome/fontawesome-svg-core'
import type { CarLocation } from '@/utils/carLocation'
import {
  appleDirectionsUrl,
  formatCoordinates,
  geoUri,
  googleDirectionsUrl,
  osmDirectionsUrl,
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
interface MapOption {
  key: 'google' | 'apple' | 'mapApp'
  href: string
  icon: IconProp
  /** A web map opens in its own tab; the geo: hand-over to an app must not leave a blank one. */
  web: boolean
}

// OpenStreetMap is the default; these are the other ways to get there.
const otherOptions = computed<MapOption[]>(() => {
  const point = destination.value
  if (!point) return []
  const { latitude: lat, longitude: lng } = point
  return [
    { key: 'google', href: googleDirectionsUrl(lat, lng), icon: ['fab', 'google'], web: true },
    { key: 'apple', href: appleDirectionsUrl(lat, lng), icon: ['fab', 'apple'], web: true },
    {
      key: 'mapApp',
      href: geoUri(lat, lng, t('directions.car')),
      icon: 'mobile-screen',
      web: false,
    },
  ]
})
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
        :href="osmDirectionsUrl(destination.latitude, destination.longitude)"
        target="_blank"
        rel="noopener noreferrer"
        :aria-describedby="descriptionId"
      >
        <font-awesome-icon icon="map" aria-hidden="true" />
        {{ t('directions.osm') }}
      </a>
      <button v-else type="button" class="btn btn-primary location-directions__primary" disabled>
        <font-awesome-icon icon="map" aria-hidden="true" />
        {{ t('directions.osm') }}
      </button>

      <template v-if="destination">
        <a
          v-for="option in otherOptions"
          :key="option.key"
          class="btn btn-outline-secondary location-directions__option"
          :href="option.href"
          :target="option.web ? '_blank' : undefined"
          :rel="option.web ? 'noopener noreferrer' : undefined"
        >
          <font-awesome-icon :icon="option.icon" aria-hidden="true" />
          {{ t(`directions.${option.key}`) }}
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
