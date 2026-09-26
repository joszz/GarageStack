<script setup lang="ts">
import { computed } from 'vue'
import type { LevelVariant } from '@/utils/levels'

// A tank or battery level on the diagram, coloured by the same bands as the dashboard's cards.
const props = defineProps<{
  icon: string
  /** What the level is of, for the tooltip. */
  label: string
  percent: number
  variant: LevelVariant
}>()

const rounded = computed(() => Math.round(props.percent))
</script>

<template>
  <div
    class="diagram-level"
    :style="{ '--level-color': `var(--color-${variant})` }"
    :title="`${label}: ${rounded}%`"
  >
    <font-awesome-icon :icon="icon" />
    <span>{{ rounded }}%</span>
  </div>
</template>
