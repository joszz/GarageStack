<script setup lang="ts">
const props = defineProps<{
  icon: string
  label: string
  value: string | number | null
  unit?: string
  subtitle?: string
  variant?: 'success' | 'warning' | 'danger' | 'info'
  clickable?: boolean
}>()

const emit = defineEmits<{ (e: 'click'): void }>()

function emitClickIfClickable(clickable?: boolean) {
  if (clickable) emit('click')
}

function valueTitle(): string {
  if (props.value === null) return '-'
  return props.unit ? `${props.value} ${props.unit}` : String(props.value)
}

function cardAriaLabel(): string {
  return `${props.label}: ${valueTitle()}${props.subtitle ? `, ${props.subtitle}` : ''}`
}
</script>

<template>
  <div
    class="status-card"
    :class="[variant ? `status-card--${variant}` : '', clickable ? 'status-card--clickable' : '']"
    :role="clickable ? 'button' : undefined"
    :tabindex="clickable ? 0 : undefined"
    :aria-label="clickable ? cardAriaLabel() : undefined"
    @click="emitClickIfClickable(clickable)"
    @keydown.enter="emitClickIfClickable(clickable)"
    @keydown.space.prevent="emitClickIfClickable(clickable)"
  >
    <div class="status-card__icon" aria-hidden="true">
      <font-awesome-icon :icon="icon" />
    </div>
    <div class="status-card__body">
      <span class="status-card__label" :title="label">{{ label }}</span>
      <span class="status-card__value" :title="valueTitle()">
        {{ value ?? '-'
        }}<span v-if="unit && value !== null" class="status-card__unit"> {{ unit }}</span>
      </span>
      <span v-if="subtitle" class="status-card__subtitle" :title="subtitle">{{ subtitle }}</span>
    </div>
    <font-awesome-icon
      v-if="clickable"
      icon="chevron-right"
      class="status-card__chevron"
      aria-hidden="true"
    />
  </div>
</template>
