<script setup lang="ts">
import { onMounted, onUnmounted } from 'vue'

const props = defineProps<{ title: string; open: boolean; wide?: boolean }>()
const emit = defineEmits<{ (e: 'close'): void }>()

function onKey(e: KeyboardEvent) {
  if (e.key === 'Escape' && props.open) emit('close')
}
onMounted(() => document.addEventListener('keydown', onKey))
onUnmounted(() => document.removeEventListener('keydown', onKey))
</script>

<template>
  <Teleport to="body">
    <Transition name="modal">
      <div v-if="open" class="detail-modal-backdrop" @click.self="emit('close')">
        <div
          class="detail-modal"
          :class="{ 'detail-modal--wide': wide }"
          role="dialog"
          :aria-modal="true"
          :aria-label="title"
        >
          <div class="detail-modal__header">
            <h3 class="detail-modal__title">{{ title }}</h3>
            <button class="detail-modal__close" aria-label="Close" @click="emit('close')">
              <font-awesome-icon icon="xmark" />
            </button>
          </div>
          <div class="detail-modal__body">
            <slot />
          </div>
          <div v-if="$slots.footer" class="detail-modal__footer">
            <slot name="footer" />
          </div>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>
