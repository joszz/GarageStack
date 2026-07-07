<script setup lang="ts">
import { useI18n } from 'vue-i18n'

const { t } = useI18n()

withDefaults(
  defineProps<{
    visible: boolean
    /** False for the two fixed-position dashboard slots (tyre diagram, location map) that aren't part of the draggable list. */
    draggable?: boolean
  }>(),
  { draggable: true },
)

defineEmits<{ 'toggle-visible': [] }>()
</script>

<template>
  <div class="card-slot" :class="{ 'card-slot--hidden': !visible }">
    <div v-if="draggable" class="card-slot__handle">
      <font-awesome-icon icon="grip-lines" />
    </div>
    <div class="card-slot__content">
      <slot />
    </div>
    <button
      class="card-slot__badge"
      :class="visible ? 'card-slot__badge--hide' : 'card-slot__badge--show'"
      :aria-label="visible ? t('dashboard.hideCard') : t('dashboard.showCard')"
      @click.stop="$emit('toggle-visible')"
    >
      <font-awesome-icon :icon="visible ? 'xmark' : 'plus'" />
    </button>
  </div>
</template>
