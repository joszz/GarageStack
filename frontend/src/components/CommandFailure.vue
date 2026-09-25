<script setup lang="ts">
import { useI18n } from 'vue-i18n'

// Why a command did not go through. With a detail, the car refused it and the detail is the
// gateway's own reason, in its words (English whatever the interface language), so it follows the
// translated message rather than replacing it. Without one, the request never reached the car.
defineProps<{ detail: string | null }>()

const { t } = useI18n()
</script>

<template>
  <span class="command-failure text-danger" role="alert">
    {{ detail ? t('control.rejected') : t('control.error') }}
    <span v-if="detail" class="command-failure__detail">{{ detail }}</span>
  </span>
</template>

<style scoped>
.command-failure {
  display: flex;
  flex-direction: column;
  min-width: 0;
}

.command-failure__detail {
  color: var(--color-text-muted);
  font-size: 0.85em;
  overflow-wrap: anywhere;
}
</style>
