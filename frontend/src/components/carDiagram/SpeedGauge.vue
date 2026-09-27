<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useUnits } from '@/composables/useUnits'

// A speedometer: a 270° arc opening at the bottom, with the needle sweeping from -135deg
// (standing still) to +135deg (MAX_SPEED_KMH) through the top of the dial.
const props = defineProps<{ speedKmh: number }>()

const { t } = useI18n()
const units = useUnits()

const MAX_SPEED_KMH = 200
const GAUGE_CX = 50
const GAUGE_CY = 48
const GAUGE_R = 36
const GAUGE_ARC_LENGTH = 1.5 * Math.PI * GAUGE_R

function polarToCartesian(cx: number, cy: number, r: number, bearingDeg: number) {
  const rad = (bearingDeg * Math.PI) / 180
  return { x: cx + r * Math.sin(rad), y: cy - r * Math.cos(rad) }
}

const gaugeArcPath = (() => {
  const start = polarToCartesian(GAUGE_CX, GAUGE_CY, GAUGE_R, -135)
  const end = polarToCartesian(GAUGE_CX, GAUGE_CY, GAUGE_R, 135)
  return `M ${start.x} ${start.y} A ${GAUGE_R} ${GAUGE_R} 0 1 1 ${end.x} ${end.y}`
})()

const speedMeasure = computed(() => units.value.measure('speed', props.speedKmh)!)
const speedFraction = computed(() => Math.min(1, Math.max(0, props.speedKmh / MAX_SPEED_KMH)))
const needleRotation = computed(() => -135 + speedFraction.value * 270)
const gaugeDashOffset = computed(() => GAUGE_ARC_LENGTH * (1 - speedFraction.value))

const gaugeColor = computed(() => {
  if (props.speedKmh >= 140) return 'var(--color-danger)'
  if (props.speedKmh >= 100) return 'var(--color-warning)'
  return 'var(--color-primary-light)'
})

const label = computed(
  () => `${t('vehicle.speed')}: ${speedMeasure.value.value} ${speedMeasure.value.unit}`,
)
</script>

<template>
  <div class="speed-gauge" role="img" :aria-label="label" :title="label">
    <svg viewBox="0 0 100 92" class="speed-gauge__svg">
      <path :d="gaugeArcPath" class="speed-gauge__track" />
      <path
        :d="gaugeArcPath"
        class="speed-gauge__progress"
        :style="{
          strokeDasharray: GAUGE_ARC_LENGTH,
          strokeDashoffset: gaugeDashOffset,
          stroke: gaugeColor,
        }"
      />
      <line
        x1="50"
        y1="48"
        x2="50"
        y2="20"
        class="speed-gauge__needle"
        :style="{ transform: `rotate(${needleRotation}deg)` }"
      />
      <circle cx="50" cy="48" r="3" class="speed-gauge__pivot" />
      <text x="50" y="64" text-anchor="middle" class="speed-gauge__value">
        {{ speedMeasure.value }}
      </text>
      <text x="50" y="78" text-anchor="middle" class="speed-gauge__unit">
        {{ speedMeasure.unit }}
      </text>
    </svg>
  </div>
</template>
