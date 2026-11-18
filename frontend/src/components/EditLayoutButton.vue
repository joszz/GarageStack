<script setup lang="ts">
import { useI18n } from 'vue-i18n'

// The page header's button that opens and closes the layout editor (useEditMode).
const editing = defineModel<boolean>({ required: true })

const { t } = useI18n()
</script>

<template>
  <button
    class="btn"
    :class="editing ? 'btn-primary' : 'btn-outline-secondary'"
    @click="editing = !editing"
  >
    <font-awesome-icon :icon="editing ? 'check' : 'pen-to-square'" />
    {{ editing ? t('dashboard.doneEditing') : t('dashboard.editLayout') }}
  </button>
</template>
