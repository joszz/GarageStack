<script setup lang="ts">
import { ref, watch } from 'vue'

const props = defineProps<{
  modelValue: number
  totalPages: number
}>()

const emit = defineEmits<{
  (e: 'update:modelValue', page: number): void
}>()

const pageInput = ref(props.modelValue)

watch(() => props.modelValue, (p) => {
  pageInput.value = p
})

function commit() {
  const val = isNaN(pageInput.value)
    ? props.modelValue
    : Math.max(1, Math.min(props.totalPages, Math.round(pageInput.value)))
  emit('update:modelValue', val)
  pageInput.value = val
}

function onFocus(e: FocusEvent) {
  ;(e.target as HTMLInputElement).select()
}

function prev() {
  if (props.modelValue > 1) emit('update:modelValue', props.modelValue - 1)
}

function next() {
  if (props.modelValue < props.totalPages) emit('update:modelValue', props.modelValue + 1)
}
</script>

<template>
  <div class="trip-pagination">
    <button
      class="btn btn-sm btn-outline-secondary trip-pagination__btn"
      :disabled="modelValue === 1"
      @click="prev"
    >
      <font-awesome-icon icon="chevron-left" />
    </button>
    <input
      v-model.number="pageInput"
      type="number"
      min="1"
      :max="totalPages"
      class="trip-pagination__input"
      @blur="commit"
      @keydown.enter.prevent="commit"
      @focus="onFocus"
    >
    <span class="trip-pagination__sep">/ {{ totalPages }}</span>
    <button
      class="btn btn-sm btn-outline-secondary trip-pagination__btn"
      :disabled="modelValue === totalPages"
      @click="next"
    >
      <font-awesome-icon icon="chevron-right" />
    </button>
  </div>
</template>
