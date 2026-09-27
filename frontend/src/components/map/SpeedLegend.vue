<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useUnits } from '@/composables/useUnits'
import MapLegend from '../MapLegend.vue'

// The key to the speed overlay's colours.
const expanded = defineModel<boolean>('expanded', { required: true })

const { t } = useI18n()
const units = useUnits()

// The key's ticks, from the km/h its colours are graded in to the unit this browser shows.
const TICKS_KMH = [0, 50, 90, 130]
const ticks = computed(() => TICKS_KMH.map((kmh) => units.value.measure('speed', kmh)!.value))
</script>

<template>
  <MapLegend v-model:expanded="expanded" :label="t('trips.speedOverlay')">
    <div class="speed-legend__bar"></div>
    <div class="speed-legend__labels">
      <span v-for="(tick, i) in ticks" :key="i">{{
        i === ticks.length - 1 ? `${tick}+ ${units.symbol('speed')}` : tick
      }}</span>
    </div>
  </MapLegend>
</template>
