<script setup lang="ts" generic="T extends string | number, M extends T | T[] | null">
import type { ChipOption } from '@/utils/chips'

const props = defineProps<{
  options: ChipOption<T>[]
  /** Names the group for screen readers. */
  groupLabel: string
  disabled?: boolean
  /** One-of-N only: pressing the chosen chip again clears the choice to null. */
  deselectable?: boolean
}>()

// An array model makes this a several-of-N choice; any other model a one-of-N choice.
const model = defineModel<M>({ required: true })

function isActive(value: T): boolean {
  const current = model.value
  return Array.isArray(current) ? current.includes(value) : current === value
}

function choose(value: T) {
  const current = model.value
  if (Array.isArray(current)) {
    // Kept in the options' order, so the model reads the same whichever chip was pressed first.
    const chosen = new Set(
      isActive(value) ? current.filter((v) => v !== value) : [...current, value],
    )
    model.value = props.options.map((o) => o.value).filter((v) => chosen.has(v)) as M
    return
  }
  if (current !== value) model.value = value as M
  else if (props.deselectable) model.value = null as M
}
</script>

<template>
  <div class="toggle-chip-group" role="group" :aria-label="groupLabel">
    <button
      v-for="option in options"
      :key="option.value"
      type="button"
      class="toggle-chip"
      :class="[option.class, { 'toggle-chip--active': isActive(option.value) }]"
      :aria-pressed="isActive(option.value)"
      :disabled="disabled"
      @click="choose(option.value)"
    >
      <font-awesome-icon v-if="option.icon" :icon="option.icon" aria-hidden="true" />
      <span>{{ option.label }}</span>
    </button>
  </div>
</template>
