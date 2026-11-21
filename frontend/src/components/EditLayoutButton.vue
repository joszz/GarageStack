<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import '@/assets/editMode.css'

/**
 * Opens and closes the layout editor (useEditMode). With a mouse the header button does both.
 * On a touchscreen a long press on a card opens the editor and a bar floating at the bottom of the
 * screen closes it, as on a phone's home screen, so the header button gives way: while editing,
 * and while a card is there to press (`cardsShown`). With no card on the page (all hidden, or no
 * data) the button stays, or the editor could not be opened at all. Which of the two a screen
 * gets is up to CSS (editMode.css).
 */
const editing = defineModel<boolean>({ required: true })
defineProps<{ cardsShown: boolean }>()

const { t } = useI18n()
</script>

<template>
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
        <span class="edit-mode-bar__label">{{ t('dashboard.editingLayout') }}</span>
        <button class="btn btn-primary" @click="editing = false">
          <font-awesome-icon icon="check" />
          {{ t('dashboard.doneEditing') }}
        </button>
      </div>
    </Transition>
  </Teleport>
</template>
