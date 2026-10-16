<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useEscapeKey } from '@/composables/useEscapeKey'
import { useDialogFocus } from '@/composables/useDialogFocus'

const { t } = useI18n()
const props = defineProps<{ title: string; open: boolean; wide?: boolean }>()
const emit = defineEmits<{ (e: 'close'): void }>()

useEscapeKey(
  () => props.open,
  () => emit('close'),
)

const dialog = ref<HTMLElement | null>(null)
useDialogFocus(dialog, () => props.open)
</script>

<template>
  <Teleport to="body">
    <Transition name="modal">
      <div v-if="open" class="detail-modal-backdrop" @click.self="emit('close')">
        <div
          ref="dialog"
          class="detail-modal"
          :class="{ 'detail-modal--wide': wide }"
          tabindex="-1"
          role="dialog"
          :aria-modal="true"
          :aria-label="title"
        >
          <div class="detail-modal__header">
            <h3 class="detail-modal__title">{{ title }}</h3>
            <button
              class="detail-modal__close"
              :aria-label="t('common.close')"
              @click="emit('close')"
            >
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
