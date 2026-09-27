<script setup lang="ts">
import SettingsToggle from '../SettingsToggle.vue'

// A switch between two named options, each shown on its own side: dark and light, English and
// Dutch. The side that is in effect is highlighted.
defineProps<{
  label: string
  desc?: string
  inputId: string
  /** The sides hold flag images rather than icons. */
  flags?: boolean
}>()

const on = defineModel<boolean>({ required: true })
</script>

<template>
  <SettingsToggle :label="label" :desc="desc" :input-id="inputId">
    <template #control>
      <span
        class="settings-toggle__side-icon"
        :class="{
          'settings-toggle__side-icon--flag': flags,
          'settings-toggle__side-icon--active': !on,
        }"
      >
        <slot name="off" />
      </span>
      <div class="form-check form-switch mb-0">
        <input
          :id="inputId"
          v-model="on"
          class="form-check-input"
          type="checkbox"
          role="switch"
          :aria-label="label"
        />
      </div>
      <span
        class="settings-toggle__side-icon"
        :class="{
          'settings-toggle__side-icon--flag': flags,
          'settings-toggle__side-icon--active': on,
        }"
      >
        <slot name="on" />
      </span>
    </template>
  </SettingsToggle>
</template>
