<script setup lang="ts">
import '@/assets/dashboard.css'
import { useI18n } from 'vue-i18n'
import { useCarLocation, useParkedAddress } from '@/composables/useCarLocation'
import { OSM_COPYRIGHT_URL } from '@/utils/credits'
import LocationMapActions from './LocationMapActions.vue'

/**
 * The room the location card leaves free when the map lies behind the dashboard
 * (DashboardMapBackdrop): the car stands in its middle, under a bar that says where that is and
 * holds the location card's buttons. The bar also carries the map's OpenStreetMap credit, which
 * the map cannot show itself: its corners lie under the cards.
 */
const { t } = useI18n()
const car = useCarLocation()
const { location, hasLocation, openFullMap } = car
const { address, addressPending } = useParkedAddress(car)
</script>

<template>
  <div class="parking-spot">
    <div class="parking-spot__bar">
      <font-awesome-icon icon="location-dot" class="parking-spot__icon" />
      <!-- Held to select and copy, so not a long press into the layout editor (useLongPress). -->
      <p v-if="address" class="parking-spot__label long-press-exempt" :title="address">
        {{ address }}
      </p>
      <p v-else-if="addressPending" class="parking-spot__label">
        <span
          class="skeleton skeleton--text skeleton--text-md"
          role="status"
          :aria-label="t('vehicle.locatingAddress')"
        />
      </p>
      <p v-else class="parking-spot__label">
        {{ hasLocation ? t('vehicle.location') : t('vehicle.locationUnavailable') }}
      </p>
      <a class="parking-spot__credit" :href="OSM_COPYRIGHT_URL" target="_blank" rel="noreferrer"
        >&copy; OpenStreetMap</a
      >
      <LocationMapActions :location="location" @open-full-map="openFullMap" />
    </div>
  </div>
</template>
