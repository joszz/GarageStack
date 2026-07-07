<script setup lang="ts">
import { useI18n } from 'vue-i18n'

const { t } = useI18n()

withDefaults(
  defineProps<{
    /** Whether a prior send for this command is still awaiting vehicle confirmation. */
    pending: boolean
    /** Whether the send request itself is currently in flight. */
    sending: boolean
    /** Extra condition to disable the button beyond sending/pending (e.g. no VIN yet). */
    disabled?: boolean
    /** Idle-state icon. Omit to render no icon at all, in any state (matches icon-less buttons). */
    icon?: string
    label: string
    /** Set false when several buttons share one command key, so pending doesn't blank every label. */
    showPendingLabel?: boolean
  }>(),
  { disabled: false, showPendingLabel: true },
)

defineEmits<{ click: [] }>()
</script>

<template>
  <button
    class="btn"
    :class="{ 'btn--pending': pending }"
    :disabled="sending || pending || disabled"
    @click="$emit('click')"
  >
    <template v-if="icon">
      <font-awesome-icon v-if="sending" icon="spinner" spin />
      <font-awesome-icon v-else-if="pending" icon="clock" />
      <font-awesome-icon v-else :icon="icon" />
    </template>
    {{ pending && showPendingLabel ? t('control.pending') : label }}
  </button>
</template>
