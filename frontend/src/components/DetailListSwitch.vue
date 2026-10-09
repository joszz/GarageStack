<script setup lang="ts">
defineProps<{
  icon: string
  label: string
  disabled?: boolean
}>()

// Null (not known yet) shows as off, and switching it turns it on.
const model = defineModel<boolean | null>({ required: true })
</script>

<template>
  <div class="detail-list__item detail-list__item--control">
    <font-awesome-icon :icon="icon" class="detail-list__item-icon" />
    <span class="detail-list__item-label">{{ label }}</span>
    <div class="form-check form-switch">
      <input
        class="form-check-input"
        type="checkbox"
        role="switch"
        :checked="model === true"
        :disabled="disabled"
        :aria-label="label"
        @change="model = !model"
      />
    </div>
  </div>
</template>
