<script setup lang="ts">
defineProps<{
  icon: string
  label: string
  value: string | number | null
  unit?: string
  variant?: 'success' | 'warning' | 'danger' | 'info'
  clickable?: boolean
}>()

const emit = defineEmits<{ (e: 'click'): void }>()
</script>

<template>
  <div
    class="status-card"
    :class="[variant ? `status-card--${variant}` : '', clickable ? 'status-card--clickable' : '']"
    :role="clickable ? 'button' : undefined"
    :tabindex="clickable ? 0 : undefined"
    @click="clickable && emit('click')"
    @keydown.enter="clickable && emit('click')"
    @keydown.space.prevent="clickable && emit('click')"
  >
    <div class="status-card__icon">
      <font-awesome-icon :icon="icon" />
    </div>
    <div class="status-card__body">
      <span class="status-card__label">{{ label }}</span>
      <span class="status-card__value">
        {{ value ?? '-' }}<span v-if="unit && value !== null" class="status-card__unit"> {{ unit }}</span>
      </span>
    </div>
    <font-awesome-icon v-if="clickable" icon="chevron-right" class="status-card__chevron" />
  </div>
</template>
