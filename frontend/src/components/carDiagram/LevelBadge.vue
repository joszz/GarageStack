<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { LevelVariant } from '@/utils/levels'

// A tank or battery level on the diagram, coloured by the same bands as the dashboard's cards.
// Tapping it opens that level's history.
const props = defineProps<{
  icon: string
  /** What the level is of, for the tooltip. */
  label: string
  percent: number
  variant: LevelVariant
}>()
defineEmits<{ select: [] }>()

const { t } = useI18n()

const rounded = computed(() => Math.round(props.percent))
</script>

<template>
  <button
    type="button"
    class="diagram-level"
    :class="`diagram-level--${variant}`"
    :title="`${label}: ${rounded}%`"
    :aria-label="t('levelHistory.open', { level: label, reading: `${rounded}%` })"
    @click="$emit('select')"
  >
    <font-awesome-icon :icon="icon" />
    <span>{{ rounded }}%</span>
  </button>
</template>
