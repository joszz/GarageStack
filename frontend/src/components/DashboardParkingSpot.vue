<script setup lang="ts">
import '@/assets/dashboard.css'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useCarLocation, useParkedAddress } from '@/composables/useCarLocation'
import type { BasemapKind } from '@/composables/useBasemap'
import { BASEMAP_CREDIT_LINKS } from '@/utils/credits'
import LocationMapActions from './LocationMapActions.vue'

/**
 * The room the location card leaves free when the map lies behind the dashboard
 * (DashboardMapBackdrop): the car stands in its middle, under a bar that says where that is and
 * holds the location card's buttons. The bar also carries the credit of the basemap the map
 * draws, which the map cannot show itself: its corners lie under the cards.
 */
const props = defineProps<{
  /** The basemap the map behind draws, or null while it has none yet. */
  basemap: BasemapKind | null
}>()

const { t } = useI18n()
const credits = computed(() => (props.basemap ? BASEMAP_CREDIT_LINKS[props.basemap] : []))
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
      <span v-if="credits.length" class="parking-spot__credit">
        <template v-for="(credit, index) in credits" :key="credit.href">
          <template v-if="index > 0"> &middot; </template>
          <a :href="credit.href" target="_blank" rel="noreferrer">{{ credit.text }}</a>
        </template>
      </span>
      <LocationMapActions :location="location" @open-full-map="openFullMap" />
    </div>
  </div>
</template>
