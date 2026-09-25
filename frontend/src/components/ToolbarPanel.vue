<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import DetailModal from './DetailModal.vue'

/**
 * A view-header button that opens a modal of settings rows. Defaults to the filters button every
 * view had; a title and icon turn it into any other panel, such as the map's layer switcher.
 *
 * @param count How many of the panel's settings are doing something right now, shown as a badge
 *   on the button so the panel does not have to be opened to find out. Hidden when zero.
 */
const props = defineProps<{ title?: string; icon?: string; count?: number }>()

const { t } = useI18n()
const isOpen = ref(false)

const label = computed(() => props.title ?? t('common.filters'))
const iconName = computed(() => props.icon ?? 'sliders')
const activeCount = computed(() => props.count ?? 0)

// The badge is a glyph, so the count reaches a screen reader through the button's own name
// rather than as a number read out after it with nothing to attach it to.
const buttonLabel = computed(() =>
  activeCount.value > 0
    ? t('common.panelWithCount', { label: label.value, n: activeCount.value })
    : label.value,
)
</script>

<template>
  <div>
    <button
      class="btn btn-sm btn-outline-secondary toolbar-panel__btn"
      :aria-label="buttonLabel"
      @click="isOpen = true"
    >
      <font-awesome-icon :icon="iconName" />{{ label }}
      <span v-if="activeCount > 0" class="toolbar-panel__count" aria-hidden="true">
        {{ activeCount }}
      </span>
    </button>
    <DetailModal :open="isOpen" :title="label" @close="isOpen = false">
      <div class="settings-toggles">
        <slot />
      </div>
    </DetailModal>
  </div>
</template>

<style scoped>
.toolbar-panel__btn {
  position: relative;
}

/* Sits over the button's top-right corner rather than inside it, so a count appearing never
   changes the button's width and nudges the rest of the header along. */
.toolbar-panel__count {
  position: absolute;
  top: 0;
  right: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  min-width: 1.05rem;
  height: 1.05rem;
  padding: 0 0.2rem;
  transform: translate(35%, -35%);
  border: 2px solid var(--color-bg);
  border-radius: 999px;
  background: var(--color-primary);
  color: #fff;
  font-size: 0.65rem;
  font-weight: 700;
  line-height: 1;
}
</style>
