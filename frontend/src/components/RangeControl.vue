<script setup lang="ts">
withDefaults(
  defineProps<{
    icon: string
    label: string
    /** The current value as shown next to the label, e.g. "21 °C" or "High". */
    valueText: string
    min: number
    max: number
    step?: number
    /** Shown before and after the slider, e.g. its lowest and highest temperature. */
    edgeLabels?: [string, string]
    /** One label per step, spread out under the slider. */
    tickLabels?: string[]
    disabled?: boolean
  }>(),
  { step: 1, edgeLabels: undefined, tickLabels: undefined },
)

const model = defineModel<number>({ required: true })
</script>

<template>
  <div class="detail-list__item detail-list__item--range">
    <font-awesome-icon :icon="icon" class="detail-list__item-icon" />
    <div class="range-control range-control--grow">
      <div class="range-control__header">
        <span class="range-control__label">{{ label }}</span>
        <span class="range-control__value">{{ valueText }}</span>
      </div>
      <div class="range-control__row">
        <span v-if="edgeLabels">{{ edgeLabels[0] }}</span>
        <input
          v-model.number="model"
          type="range"
          :min="min"
          :max="max"
          :step="step"
          :disabled="disabled"
          :aria-label="label"
          :aria-valuetext="valueText"
        />
        <span v-if="edgeLabels">{{ edgeLabels[1] }}</span>
      </div>
      <div v-if="tickLabels" class="range-control__labels">
        <span v-for="tick in tickLabels" :key="tick">{{ tick }}</span>
      </div>
    </div>
  </div>
</template>
