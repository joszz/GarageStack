<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useTwoTapConfirm } from '@/composables/useTwoTapConfirm'

const { t } = useI18n()

const props = withDefaults(
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
    /**
     * For a command someone could misuse with a phone left unlocked (opening the boot): the first
     * tap only shows this label, and a second tap within a few seconds sends.
     */
    confirmLabel?: string
  }>(),
  { disabled: false, showPendingLabel: true, confirmLabel: undefined },
)

const emit = defineEmits<{ click: [] }>()

// One button, so the confirmation has a single key.
const confirmation = useTwoTapConfirm<true>()
const armed = computed(() => confirmation.armed.value !== null)
const disarm = confirmation.disarm

function onClick() {
  if (!props.confirmLabel || confirmation.tap(true)) emit('click')
}
</script>

<template>
  <button
    class="btn"
    :class="{ 'btn--pending': pending, 'btn--armed': armed }"
    :disabled="sending || pending || disabled"
    @click="onClick"
    @blur="disarm"
  >
    <template v-if="icon">
      <font-awesome-icon v-if="sending" icon="spinner" spin />
      <font-awesome-icon v-else-if="pending" icon="clock" />
      <font-awesome-icon v-else :icon="armed ? 'triangle-exclamation' : icon" />
    </template>
    {{ armed ? confirmLabel : pending && showPendingLabel ? t('control.pending') : label }}
  </button>
</template>
