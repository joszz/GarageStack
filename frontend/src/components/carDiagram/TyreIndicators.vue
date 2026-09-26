<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useTyreDisplay, type TyrePosition, type TyrePressures } from './useTyreDisplay'

// The coloured line and dot at each wheel, drawn inside the diagram's SVG. The coordinates come
// from the car body's bounds; see the ViewBox comment in CarDiagram.vue.
const props = defineProps<{ pressures: TyrePressures }>()

const { t } = useI18n()
const { color, reading } = useTyreDisplay()

const INDICATORS: { key: TyrePosition; x1: number; y1: number; x2: number; y2: number }[] = [
  { key: 'frontLeft', x1: 110, y1: 145, x2: 72, y2: 131 },
  { key: 'frontRight', x1: 230, y1: 145, x2: 268, y2: 131 },
  { key: 'rearLeft', x1: 110, y1: 327, x2: 72, y2: 341 },
  { key: 'rearRight', x1: 230, y1: 327, x2: 268, y2: 341 },
]

const indicators = computed(() =>
  INDICATORS.map((indicator) => ({ ...indicator, value: props.pressures[indicator.key] })),
)
</script>

<template>
  <g v-for="tyre in indicators" :key="tyre.key">
    <title>{{ t(`vehicle.diagram.tyrePosition.${tyre.key}`) }}: {{ reading(tyre.value) }}</title>
    <line
      :x1="tyre.x1"
      :y1="tyre.y1"
      :x2="tyre.x2"
      :y2="tyre.y2"
      class="tyre-indicator-line"
      :stroke="color(tyre.value)"
    />
    <circle
      :cx="tyre.x1"
      :cy="tyre.y1"
      r="5"
      class="tyre-indicator-dot"
      :fill="color(tyre.value)"
    />
  </g>
</template>
