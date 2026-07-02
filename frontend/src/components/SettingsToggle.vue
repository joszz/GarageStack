<script setup lang="ts">
defineProps<{
  label: string
  desc?: string
  inputId?: string
}>()

const model = defineModel<boolean>()
</script>

<template>
  <div class="settings-toggle">
    <div class="settings-toggle__info">
      <slot name="label">
        <span class="settings-toggle__label">{{ label }}</span>
      </slot>
      <span v-if="desc" class="settings-toggle__desc text-muted">{{ desc }}</span>
    </div>
    <div class="settings-toggle__control">
      <slot name="control">
        <div class="form-check form-switch mb-0">
          <input
            :id="inputId"
            v-model="model"
            class="form-check-input"
            type="checkbox"
            role="switch"
            :aria-label="label"
          />
        </div>
      </slot>
    </div>
  </div>
</template>
