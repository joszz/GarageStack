<script setup lang="ts" generic="T extends { id: string; visible: boolean }">
import { VueDraggable } from 'vue-draggable-plus'
import EditableCardSlot from './EditableCardSlot.vue'

// The edit-mode grid the dashboard and statistics views share: cards dragged by their handle,
// each with a badge to hide or show it. The grid's own layout class comes from the caller.
const items = defineModel<T[]>({ required: true })

defineProps<{
  /** Class for each card's slot, e.g. to size chart slots. */
  slotClass?: string
  /** Cards that do not apply to this car stay in the list but out of sight. */
  isShown?: (item: T) => boolean
}>()

defineEmits<{ 'toggle-visible': [item: T] }>()

defineSlots<{ default(props: { item: T }): unknown }>()
</script>

<template>
  <VueDraggable
    v-model="items"
    :animation="200"
    ghost-class="card-slot--ghost"
    chosen-class="card-slot--chosen"
    handle=".card-slot__handle"
  >
    <EditableCardSlot
      v-for="item in items"
      v-show="isShown?.(item) ?? true"
      :key="item.id"
      :class="slotClass"
      :visible="item.visible"
      @toggle-visible="$emit('toggle-visible', item)"
    >
      <slot :item="item" />
    </EditableCardSlot>
  </VueDraggable>
</template>
