<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import type { CarLocation } from '@/utils/carLocation'
import LocationDirections from './LocationDirections.vue'

/**
 * The buttons over the car's map: directions to where it is parked, and the full map. A click on
 * them ends here, so it never also lands on a map that opens the full map when clicked.
 */
defineProps<{ location: CarLocation | null }>()
const emit = defineEmits<{ openFullMap: [] }>()

const { t } = useI18n()
const directionsOpen = ref(false)
</script>

<template>
  <div class="location-map-card__actions" @click.stop>
    <button
      type="button"
      class="location-map-card__directions"
      :aria-label="t('directions.open')"
      :title="t('directions.open')"
      aria-haspopup="dialog"
      @click="directionsOpen = true"
    >
      <font-awesome-icon icon="diamond-turn-right" aria-hidden="true" />
    </button>
    <button
      type="button"
      class="location-map-card__expand"
      :aria-label="t('dashboard.openFullMap')"
      :title="t('dashboard.openFullMap')"
      @click="emit('openFullMap')"
    >
      <font-awesome-icon icon="map" />
    </button>
    <LocationDirections
      :location="location"
      :open="directionsOpen"
      @close="directionsOpen = false"
    />
  </div>
</template>
