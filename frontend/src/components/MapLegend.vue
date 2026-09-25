<script setup lang="ts">
import { useId } from 'vue'
import { useI18n } from 'vue-i18n'

/**
 * A key laid over the map, under the zoom control. It folds away to a single button the size of a
 * zoom button, so on a phone's short map it covers nothing until it is asked for.
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
      class="map-legend__toggle"
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
/* Top left, under the zoom control. The bottom edge belongs to the attribution strip, whose width
   depends on how many sources are credited and how wide the map happens to be: on a narrow map, in
   any layout, it reaches the bottom-left corner this legend used to sit in. Keeping the legend off
   that edge entirely is one rule instead of a breakpoint per layout. */
.map-legend {
  position: absolute;
  top: 80px;
  left: 10px;
  z-index: 1000;
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 6px;

  /* A phone-width map needs the open key to stay inside it. */
  max-width: calc(100% - 20px);

  /* Only the button takes clicks: the map under the rest of the key stays draggable. */
  pointer-events: none;
}

/* Dressed as one more button of the zoom control above it, whose frame is Leaflet's touch one:
   a 30px button inside a 2px translucent border, and no shadow. */
.map-legend__toggle {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 34px;
  height: 34px;
  padding: 0;
  border: 2px solid rgb(0 0 0 / 20%);
  border-radius: 4px;
  background: var(--color-surface) padding-box;
  color: var(--color-text);
  cursor: pointer;
  pointer-events: auto;
}

.map-legend__toggle:hover {
  background: var(--color-surface-2);
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
