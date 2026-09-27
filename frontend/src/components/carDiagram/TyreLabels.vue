<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useTyreDisplay, type TyrePosition, type TyrePressures } from './useTyreDisplay'

// The pressure readings laid over the diagram at the end of each wheel's indicator line.
const props = defineProps<{ pressures: TyrePressures }>()

const { t } = useI18n()
const { variant, reading } = useTyreDisplay()

const LABELS: { key: TyrePosition; suffix: string }[] = [
  { key: 'frontLeft', suffix: 'fl' },
  { key: 'frontRight', suffix: 'fr' },
  { key: 'rearLeft', suffix: 'rl' },
  { key: 'rearRight', suffix: 'rr' },
]

const labels = computed(() =>
  LABELS.map((label) => ({ ...label, value: props.pressures[label.key] })),
)
</script>

<template>
  <div class="tyre-labels">
    <div
      v-for="label in labels"
      :key="label.key"
      :class="['tyre-label', `tyre-label--${label.suffix}`, `tyre-label--${variant(label.value)}`]"
      :title="t(`vehicle.diagram.tyrePosition.${label.key}`)"
    >
      {{ reading(label.value) }}
    </div>
  </div>
</template>
