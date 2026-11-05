<script setup lang="ts">
import { useId } from 'vue'
import { useI18n } from 'vue-i18n'

/**
 * A key laid over the map, in the map's tool column under the zoom control (map.css's
 * .map-tools). It folds away to a single button the size of a zoom button, so on a phone's short
 * map it covers nothing until it is asked for.
 *
 * @param label What the key explains, which names it for assistive tech.
 * @model expanded Whether the key is open. Owned by the parent so it outlives the key itself: the
 *   key unmounts and comes back each time another trip is selected, and should come back as the
 *   user left it.
 */
defineProps<{ label: string }>()
const expanded = defineModel<boolean>('expanded', { required: true })

const { t } = useI18n()
const panelId = useId()
</script>

<template>
  <div class="map-legend">
    <button
      type="button"
      class="map-control-btn map-legend__toggle"
      :aria-expanded="expanded"
      :aria-controls="panelId"
      :aria-label="t('trips.legend')"
      :title="t('trips.legend')"
      @click="expanded = !expanded"
    >
      <font-awesome-icon :icon="expanded ? 'xmark' : 'circle-info'" />
    </button>
    <div v-show="expanded" :id="panelId" class="map-legend__panel" role="group" :aria-label="label">
      <slot />
    </div>
  </div>
</template>

<style scoped>
/* The column it sits in places it; the key only stacks its button over its panel. */
.map-legend {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 6px;
  max-width: 100%;
}

.map-legend__toggle[aria-expanded='true'] {
  color: var(--color-primary-light);
}

.map-legend__panel {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius);
  padding: 6px 8px;
  box-shadow: 0 1px 4px rgb(0 0 0 / 20%);
  font-size: 0.7rem;
  min-width: 130px;

  /* The speed-limit key carries a sentence rather than a row of numbers, so it needs a ceiling
     to wrap against. */
  max-width: 260px;
}
</style>
