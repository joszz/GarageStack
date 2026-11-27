<script setup lang="ts">
import '@/assets/editMode.css'
import { useI18n } from 'vue-i18n'

const { t } = useI18n()

// The two magnifier buttons laid over a card in edit mode, to make it larger or smaller. What a
// step does, and where it stops, is up to the card: a button at its end is only dimmed. It is
// aria-disabled rather than disabled, so a finger or a key that has just taken the last step does
// not lose its focus to the page as the button it was on switches off.
const props = defineProps<{
  /** What the buttons resize, for a screen reader. */
  label: string
  canLarger: boolean
  canSmaller: boolean
}>()

const emit = defineEmits<{ larger: []; smaller: [] }>()

function larger() {
  if (props.canLarger) emit('larger')
}

function smaller() {
  if (props.canSmaller) emit('smaller')
}
</script>

<template>
  <div class="card-slot__size" role="group" :aria-label="label">
    <button
      type="button"
      class="card-slot__size-btn"
      :aria-disabled="!canSmaller"
      :aria-label="t('dashboard.cardSmaller')"
      @click.stop="smaller"
    >
      <font-awesome-icon icon="magnifying-glass-minus" />
    </button>
    <button
      type="button"
      class="card-slot__size-btn"
      :aria-disabled="!canLarger"
      :aria-label="t('dashboard.cardLarger')"
      @click.stop="larger"
    >
      <font-awesome-icon icon="magnifying-glass-plus" />
    </button>
  </div>
</template>
