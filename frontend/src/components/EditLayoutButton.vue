<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import '@/assets/editMode.css'

/**
 * Opens, resets (`reset`) and closes the layout editor (useEditMode). With a mouse the header's
 * buttons do all three: while editing, Reset sits beside Done. On a touchscreen a long press on a
 * card opens the editor and a bar floating at the bottom of the screen resets and closes it, as
 * on a phone's home screen, so the header's buttons give way: while editing, and while a card is
 * there to press (`cardsShown`). With no card on the page (all hidden, or no data) the edit button
 * stays, or the editor could not be opened at all. Which of the two a screen gets is up to CSS
 * (editMode.css).
 */
const editing = defineModel<boolean>({ required: true })
defineProps<{ cardsShown: boolean }>()
const emit = defineEmits<{ reset: [] }>()

const { t } = useI18n()
</script>

<template>
  <!-- Short, as in the bar: the full words would crowd a header that wraps on a narrow window. -->
  <button
    v-if="editing"
    class="btn btn-outline-secondary edit-layout-btn--touch-hidden"
    :title="t('dashboard.resetLayout')"
    @click="emit('reset')"
  >
    <font-awesome-icon icon="rotate-left" />
    {{ t('dashboard.resetLayoutShort') }}
  </button>
  <button
    class="btn edit-layout-btn"
    :class="[
      editing ? 'btn-primary edit-layout-btn--editing' : 'btn-outline-secondary',
      { 'edit-layout-btn--touch-hidden': editing || cardsShown },
    ]"
    @click="editing = !editing"
  >
    <font-awesome-icon :icon="editing ? 'check' : 'pen-to-square'" />
    {{ editing ? t('dashboard.doneEditing') : t('dashboard.editLayout') }}
  </button>

  <Teleport to="body">
    <Transition name="edit-mode-bar">
      <div
        v-if="editing"
        class="edit-mode-bar"
        role="toolbar"
        :aria-label="t('dashboard.editingLayout')"
      >
        <button
          class="btn btn-outline-secondary"
          :title="t('dashboard.resetLayout')"
          @click="emit('reset')"
        >
          <font-awesome-icon icon="rotate-left" />
          {{ t('dashboard.resetLayoutShort') }}
        </button>
        <button class="btn btn-primary" @click="editing = false">
          <font-awesome-icon icon="check" />
          {{ t('dashboard.doneEditing') }}
        </button>
      </div>
    </Transition>
  </Teleport>
</template>
