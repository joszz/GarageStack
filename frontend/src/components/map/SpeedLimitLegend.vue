<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useUnits } from '@/composables/useUnits'
import { OVER_LIMIT_TOLERANCE_KPH, type SpeedLimitSummary } from '@/utils/speedLimits'
import MapLegend from '../MapLegend.vue'

// The key to the speed limit colours, with what they add up to for the selected trip.
const props = defineProps<{
  summary: SpeedLimitSummary
  /**
   * Whether the line is drawn in limit colours. A trip whose roads OSM holds no limit for
   * anywhere keeps its own colour, and the key says why instead.
   */
  coloured: boolean
}>()
const expanded = defineModel<boolean>('expanded', { required: true })

const { t } = useI18n()
const units = useUnits()

const coveragePct = computed(() =>
  props.summary.totalKm > 0 ? Math.round((props.summary.knownKm / props.summary.totalKm) * 100) : 0,
)

const overPct = computed(() =>
  props.summary.knownKm > 0 ? Math.round((props.summary.overKm / props.summary.knownKm) * 100) : 0,
)
</script>

<template>
  <MapLegend v-model:expanded="expanded" :label="t('trips.speedLimitOverlay')">
    <template v-if="coloured">
      <div class="speed-legend__keys">
        <span class="speed-legend__key">
          <span class="speed-legend__swatch speed-legend__swatch--under"></span>
          {{ t('trips.speedLimitUnder') }}
        </span>
        <span class="speed-legend__key">
          <span class="speed-legend__swatch speed-legend__swatch--over"></span>
          {{ t('trips.speedLimitOver') }}
        </span>
        <span class="speed-legend__key">
          <span class="speed-legend__swatch speed-legend__swatch--unknown"></span>
          {{ t('trips.speedLimitUnknown') }}
        </span>
      </div>
      <div
        class="speed-legend__summary"
        :title="
          t('trips.speedLimitTolerance', {
            speed: units.format('speed', OVER_LIMIT_TOLERANCE_KPH),
          })
        "
      >
        <span v-if="summary.overKm > 0">
          {{
            t('trips.speedLimitOverSummary', {
              distance: units.format('distance', summary.overKm),
              pct: overPct,
              speed: units.format('speed', summary.maxOverKph),
            })
          }}
        </span>
        <span v-else>{{ t('trips.speedLimitNoneOver') }}</span>
        <span class="speed-legend__coverage">
          {{ t('trips.speedLimitCoverage', { pct: coveragePct }) }}
        </span>
      </div>
    </template>
    <div v-else class="speed-legend__summary">{{ t('trips.speedLimitUnmapped') }}</div>
  </MapLegend>
</template>
