<script setup lang="ts">
// A page's title row: the heading, the page's actions on the right, an optional caption under the
// heading inside the row (the period Statistics shows), and an optional line about the page under
// the row.
defineProps<{
  title: string
  caption?: string
  subtitle?: string
}>()
</script>

<template>
  <div class="view-header">
    <div v-if="caption">
      <h1>{{ title }}</h1>
      <p class="text-muted text-sm view-header__caption">{{ caption }}</p>
    </div>
    <h1 v-else>{{ title }}</h1>
    <div v-if="$slots.actions" class="view-header__actions">
      <slot name="actions" />
    </div>
  </div>
  <p v-if="subtitle" class="text-muted view-subtitle">{{ subtitle }}</p>
</template>
