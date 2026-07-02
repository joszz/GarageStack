<script setup lang="ts">
defineProps<{
  icon: string
  label: string
  value?: string | number | null
  alert?: boolean
}>()
</script>

<template>
  <div class="detail-list__item" :class="alert ? 'detail-list__item--alert' : ''">
    <font-awesome-icon :icon="icon" class="detail-list__item-icon" />
    <slot name="value">
      <span class="detail-list__item-value">{{ value }}</span>
    </slot>
    <span class="detail-list__item-sep">-</span>
    <span class="detail-list__item-label">{{ label }}</span>
  </div>
</template>
