<script setup lang="ts">
import StatusCard from './StatusCard.vue'
import DetailModal from './DetailModal.vue'

withDefaults(
  defineProps<{
    icon: string
    title: string
    value: string | number | null
    variant?: 'success' | 'warning' | 'danger' | 'info'
    clickable?: boolean
  }>(),
  { clickable: true },
)

// Most cards don't need to observe the open state themselves, so this works standalone.
// Cards that do (e.g. resetting local edit state when the modal opens) can bind
// v-model:open to their own ref and watch it.
const open = defineModel<boolean>('open', { default: false })
</script>

<template>
  <StatusCard
    :icon="icon"
    :label="title"
    :value="value"
    :variant="variant"
    :clickable="clickable"
    @click="open = true"
  />

  <DetailModal :open="open" :title="title" @close="open = false">
    <slot :close="() => (open = false)" />
    <template v-if="$slots.footer" #footer>
      <slot name="footer" :close="() => (open = false)" />
    </template>
  </DetailModal>
</template>
