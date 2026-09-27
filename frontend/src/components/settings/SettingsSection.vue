<script setup lang="ts">
defineProps<{ title: string }>()
</script>

<template>
  <div class="detail-modal__section">
    <div class="detail-modal__section-title">{{ title }}</div>
    <slot />
  </div>
</template>
