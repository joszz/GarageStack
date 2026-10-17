<script setup lang="ts">
import { useI18n } from 'vue-i18n'

// A dialog's footer while it asks before something that cannot be undone: the question on the
// left, Cancel and Confirm on the right. `busy` holds both while the change is under way.
defineProps<{ question: string; busy?: boolean }>()
const emit = defineEmits<{ cancel: []; confirm: [] }>()
const { t } = useI18n()
</script>

<template>
  <span class="text-danger text-sm me-auto">{{ question }}</span>
  <button type="button" class="btn btn-outline-secondary" :disabled="busy" @click="emit('cancel')">
    {{ t('common.cancel') }}
  </button>
  <button type="button" class="btn btn-danger" :disabled="busy" @click="emit('confirm')">
    {{ t('common.confirm') }}
  </button>
</template>
