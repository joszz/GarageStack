<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

// The headlight beams and side markers, drawn inside the diagram's SVG behind the car. Each
// shape's base reaches under the car body (y≈140-145), so the car layer drawn on top masks it and
// the light seems to come out of the lamps. Only one headlight kind shows at a time: main beam
// over dipped beam.
const props = defineProps<{ mainBeam: boolean; dippedBeam: boolean }>()

const { t } = useI18n()

const title = computed(() => {
  if (props.mainBeam) return t('vehicle.lights.mainBeam')
  if (props.dippedBeam) return t('vehicle.lights.dippedBeam')
  return t('vehicle.lights.side')
})
</script>

<template>
  <g>
    <title>{{ title }}</title>
    <defs>
      <!--
        The beam gradients use userSpaceOnUse so their bright end sits at the car's front edge
        (y≈138), where a beam becomes visible, and fades toward its tip.
      -->
      <linearGradient
        id="beam-grad-main"
        gradientUnits="userSpaceOnUse"
        x1="170"
        y1="138"
        x2="170"
        y2="52"
      >
        <stop offset="0%" stop-color="#fef9c3" stop-opacity="0.72" />
        <stop offset="65%" stop-color="#fef9c3" stop-opacity="0.20" />
        <stop offset="100%" stop-color="#fef9c3" stop-opacity="0" />
      </linearGradient>
      <linearGradient
        id="beam-grad-dipped"
        gradientUnits="userSpaceOnUse"
        x1="170"
        y1="138"
        x2="170"
        y2="80"
      >
        <stop offset="0%" stop-color="#fde047" stop-opacity="0.65" />
        <stop offset="65%" stop-color="#fde047" stop-opacity="0.18" />
        <stop offset="100%" stop-color="#fde047" stop-opacity="0" />
      </linearGradient>
      <!-- Side lights: radial from the front fender's body edge, fading outward. -->
      <radialGradient id="beam-grad-side-l" gradientUnits="userSpaceOnUse" cx="119" cy="140" r="18">
        <stop offset="0%" stop-color="#fb923c" stop-opacity="0.70" />
        <stop offset="100%" stop-color="#fb923c" stop-opacity="0" />
      </radialGradient>
      <radialGradient id="beam-grad-side-r" gradientUnits="userSpaceOnUse" cx="221" cy="140" r="18">
        <stop offset="0%" stop-color="#fb923c" stop-opacity="0.70" />
        <stop offset="100%" stop-color="#fb923c" stop-opacity="0" />
      </radialGradient>
    </defs>
    <template v-if="mainBeam">
      <polygon points="132,145 162,140 148,52 102,62" class="car-light-beam car-light-beam--main" />
      <polygon points="208,145 178,140 192,52 238,62" class="car-light-beam car-light-beam--main" />
    </template>
    <template v-else-if="dippedBeam">
      <polygon points="130,145 148,140 144,80 118,85" class="car-light-beam" />
      <polygon points="210,145 192,140 196,80 222,85" class="car-light-beam" />
    </template>
    <!--
      Side marker pie-sectors. Centers sit inside car body (y=145) so the car SVG masks
      the source; only the outward crescent is visible. Positioned on the front fender,
      angled forward-and-outward. R=14. 170° arc sweep.
      Lower arm at SVG 90° (straight down from center) lands inside the car body so the
      arc is naturally clipped by the car layer, giving a wide visible crescent.
      Left: center (121,145), upper arm 260° → (119,131), lower arm 90° → (121,159), CCW sweep=0.
      Right: center (219,145), upper arm 280° → (221,131), lower arm 90° → (219,159), CW  sweep=1.
    -->
    <path d="M 121,145 L 119,131 A 14,14 0 0 0 121,159 Z" class="car-light-beam--side-l" />
    <path d="M 219,145 L 221,131 A 14,14 0 0 1 219,159 Z" class="car-light-beam--side-r" />
  </g>
</template>
