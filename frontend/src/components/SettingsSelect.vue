<script setup lang="ts" generic="T extends string">
import SettingsToggle from './SettingsToggle.vue'

export interface SettingsSelectOption<V extends string> {
  value: V
  label: string
}

/** A settings row whose control is a choice from a list rather than a switch. */
defineProps<{
  id: string
  label: string
  desc?: string
  options: readonly SettingsSelectOption<T>[]
}>()

const model = defineModel<T>({ required: true })
</script>

<template>
  <SettingsToggle class="settings-toggle--select" :label="label" :desc="desc">
    <template #control>
      <select
        :id="id"
        v-model="model"
        class="form-select form-select-sm settings-toggle__select"
        :aria-label="label"
      >
        <option v-for="option in options" :key="option.value" :value="option.value">
          {{ option.label }}
        </option>
      </select>
    </template>
  </SettingsToggle>
</template>
