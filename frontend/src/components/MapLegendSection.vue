<script setup lang="ts">
/**
 * One key inside the map's legend panel (MapLegend). A map can show several keys at once, so they
 * share one button and one panel, and each key is a section of it.
 *
 * @param label What the key explains, which names it for assistive tech.
 */
defineProps<{ label: string }>()
</script>

<template>
  <div class="map-legend-section" role="group" :aria-label="label">
    <slot />
  </div>
</template>

<style scoped>
/* A rule between keys, so two keys read as two rather than one long one. */
.map-legend-section + .map-legend-section {
  border-top: 1px solid var(--color-border);
  padding-top: 6px;
}
</style>
