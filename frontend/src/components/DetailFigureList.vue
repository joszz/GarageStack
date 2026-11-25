<script setup lang="ts">
import type { DetailFigure } from '@/utils/detailFigures'

/** A detail's figures as tiles: two by two beside a list, four across in a wide dialog. */
defineProps<{ figures: readonly DetailFigure[] }>()
</script>

<template>
  <dl class="detail-figures">
    <div v-for="figure in figures" :key="figure.key" class="detail-figure" :title="figure.title">
      <dt class="detail-figure__label">
        <font-awesome-icon :icon="figure.icon" aria-hidden="true" />{{ figure.label }}
      </dt>
      <dd class="detail-figure__value">
        {{ figure.measure?.value ?? '-'
        }}<span v-if="figure.measure?.unit" class="detail-figure__unit">
          {{ figure.measure.unit }}</span
        >
      </dd>
      <dd
        v-if="figure.note"
        class="detail-figure__note text-xs text-muted"
        :title="figure.note.title"
      >
        <font-awesome-icon :icon="figure.note.icon" aria-hidden="true" /> {{ figure.note.text }}
      </dd>
    </div>
  </dl>
</template>
