<script setup lang="ts">
import { ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps<{
  modelValue: number
  totalPages: number
}>()

const emit = defineEmits<{
  (e: 'update:modelValue', page: number): void
}>()

const { t } = useI18n()

const pageInput = ref(props.modelValue)

watch(
  () => props.modelValue,
  (p) => {
    pageInput.value = p
  },
)

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
  <div class="paginator">
    <button
      class="btn btn-sm btn-outline-secondary paginator__btn"
      :disabled="modelValue === 1"
      :aria-label="t('common.prevPage')"
      @click="prev"
    >
      <font-awesome-icon icon="chevron-left" aria-hidden="true" />
    </button>
    <input
      v-model.number="pageInput"
      type="number"
      min="1"
      :max="totalPages"
      class="paginator__input"
      :aria-label="t('common.pageOf', { n: modelValue, total: totalPages })"
      @blur="commit"
      @keydown.enter.prevent="commit"
      @focus="onFocus"
    />
    <span class="paginator__sep" aria-hidden="true">/ {{ totalPages }}</span>
    <button
      class="btn btn-sm btn-outline-secondary paginator__btn"
      :disabled="modelValue === totalPages"
      :aria-label="t('common.nextPage')"
      @click="next"
    >
      <font-awesome-icon icon="chevron-right" aria-hidden="true" />
    </button>
  </div>
</template>
