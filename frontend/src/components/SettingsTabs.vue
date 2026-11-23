<script setup lang="ts" generic="T extends string">
import { useTemplateRef } from 'vue'

/**
 * The row of tabs in a dialog's header that splits a long list of settings into groups: the
 * settings dialog and the map's layer panel. Goes in DetailModal's navigation slot; the parent
 * renders the panels, each with the id `${idPrefix}-panel-${tab id}` and labelled by
 * `${idPrefix}-tab-${tab id}`.
 *
 * @param shortLabel Shown on the tab instead of the label where the label is too long for the
 *   row. The label still names the tab for a screen reader and in its tooltip.
 */
const props = defineProps<{
  tabs: readonly { id: T; icon: string; label: string; shortLabel?: string }[]
  /** Names the row of tabs for a screen reader. */
  label: string
  idPrefix: string
}>()

const active = defineModel<T>({ required: true })
const tablist = useTemplateRef<HTMLElement>('tablist')

function select(tab: T, focus = false) {
  active.value = tab
  // A new tab starts at its top, not where the previous one was scrolled to.
  const body = tablist.value?.closest('.detail-modal')?.querySelector('.detail-modal__body')
  if (body) body.scrollTop = 0
  const button = tablist.value?.querySelector<HTMLButtonElement>(`[data-tab="${tab}"]`)
  if (focus) button?.focus({ preventScroll: true })
  button?.scrollIntoView({ block: 'nearest', inline: 'nearest' })
}

function onKeydown(event: KeyboardEvent) {
  const count = props.tabs.length
  const index = props.tabs.findIndex((tab) => tab.id === active.value)
  let next: number
  switch (event.key) {
    case 'ArrowRight':
      next = (index + 1) % count
      break
    case 'ArrowLeft':
      next = (index - 1 + count) % count
      break
    case 'Home':
      next = 0
      break
    case 'End':
      next = count - 1
      break
    default:
      return
  }
  event.preventDefault()
  select(props.tabs[next]!.id, true)
}

defineExpose({ select })
</script>

<template>
  <div class="settings-tabs-viewport">
    <!-- A short row has the room to spell out the chosen tab on narrower phones (see settings.css). -->
    <div
      ref="tablist"
      class="settings-tabs"
      :class="{ 'settings-tabs--few': tabs.length <= 3 }"
      role="tablist"
      :aria-label="label"
      @keydown="onKeydown"
    >
      <button
        v-for="tab in tabs"
        :id="`${idPrefix}-tab-${tab.id}`"
        :key="tab.id"
        type="button"
        role="tab"
        class="settings-tabs__tab"
        :data-tab="tab.id"
        :aria-selected="active === tab.id"
        :aria-controls="`${idPrefix}-panel-${tab.id}`"
        :aria-label="tab.label"
        :title="tab.label"
        :tabindex="active === tab.id ? 0 : -1"
        @click="select(tab.id)"
      >
        <font-awesome-icon :icon="tab.icon" aria-hidden="true" />
        <span class="settings-tabs__label">{{ tab.shortLabel ?? tab.label }}</span>
      </button>
    </div>
  </div>
</template>
