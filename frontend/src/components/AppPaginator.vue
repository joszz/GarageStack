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

function first() {
  if (props.modelValue !== 1) emit('update:modelValue', 1)
}

function prev() {
  if (props.modelValue > 1) emit('update:modelValue', props.modelValue - 1)
}

function next() {
  if (props.modelValue < props.totalPages) emit('update:modelValue', props.modelValue + 1)
}

function last() {
  if (props.modelValue !== props.totalPages) emit('update:modelValue', props.totalPages)
}
</script>

<template>
  <div class="paginator">
    <button
      class="btn btn-sm btn-outline-secondary paginator__btn"
      :disabled="modelValue === 1"
      :aria-label="t('common.firstPage')"
      @click="first"
    >
      <font-awesome-icon icon="angles-left" aria-hidden="true" />
    </button>
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
    <button
      class="btn btn-sm btn-outline-secondary paginator__btn"
      :disabled="modelValue === totalPages"
      :aria-label="t('common.lastPage')"
      @click="last"
    >
      <font-awesome-icon icon="angles-right" aria-hidden="true" />
    </button>
  </div>
</template>

<style>
.paginator {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.5rem;
  padding-top: 0.25rem;
  border-top: 1px solid var(--color-border);
}

.paginator--top {
  border-top: none;
  border-bottom: 1px solid var(--color-border);
  padding-top: 0;
  padding-bottom: 0.25rem;
}

.paginator--inline {
  border-top: none;
  border-bottom: none;
  padding: 0;
}

.paginator__btn {
  width: 28px;
  height: 28px;
  padding: 0;
  justify-content: center;
}

.paginator__input {
  width: 2.5rem;
  text-align: center;
  background: var(--color-surface-2);
  border: 1px solid var(--color-border);
  border-radius: 4px;
  color: var(--color-text);
  font-size: 0.78rem;
  padding: 0.15rem 0;
  -moz-appearance: textfield;
  font-family: inherit;
}

.paginator__input::-webkit-inner-spin-button,
.paginator__input::-webkit-outer-spin-button {
  -webkit-appearance: none;
  margin: 0;
}

.paginator__input:focus {
  outline: none;
  border-color: var(--color-primary);
}

.paginator__sep {
  font-size: 0.78rem;
  color: var(--color-text-muted);
}
</style>
