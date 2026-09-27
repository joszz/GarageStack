<script setup lang="ts">
import { nextTick, useTemplateRef, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import type { TripRow } from '@/utils/tripRows'
import { tripColorClass } from '@/utils/speedColors'

// The trip list beside the map, newest first. Each row carries the index of its trip in the
// store, which is what selection works on.
const props = defineProps<{
  rows: { realIdx: number; row: TripRow }[]
  loading: boolean
  /** There are no trips in the period at all. */
  empty: boolean
  selectedIndex: number | null
  /** The trip being driven right now, if any. */
  activeIndex: number | null
  /** How many placeholder rows to show while loading. */
  skeletonRows: number
}>()

defineEmits<{ select: [realIdx: number] }>()

const { t } = useI18n()
const root = useTemplateRef<HTMLElement>('root')
const sentinel = useTemplateRef<HTMLElement>('sentinel')

// The list scrolls inside itself and grows as its end comes into view.
defineExpose({ root, sentinel })

// A trip selected from here or from its line on the map is brought into view below the sticky
// header, without jumping when it is already visible.
watch(
  () => props.selectedIndex,
  (idx) => {
    if (idx === null) return
    nextTick(() => {
      const sidebar = root.value
      const active = sidebar?.querySelector('.trip-list__item--active') as HTMLElement | null
      if (!sidebar || !active) return
      const header = sidebar.querySelector('.trip-sidebar__header') as HTMLElement | null
      const headerHeight = header?.offsetHeight ?? 0
      const sidebarRect = sidebar.getBoundingClientRect()
      const itemRect = active.getBoundingClientRect()
      const itemTop = itemRect.top - sidebarRect.top
      const itemBottom = itemRect.bottom - sidebarRect.top
      if (itemTop < headerHeight) {
        sidebar.scrollBy({ top: itemTop - headerHeight, behavior: 'smooth' })
      } else if (itemBottom > sidebarRect.height) {
        sidebar.scrollBy({ top: itemBottom - sidebarRect.height, behavior: 'smooth' })
      }
    })
  },
)
</script>

<template>
  <aside ref="root" class="trip-sidebar">
    <div class="trip-sidebar__header">
      <h2 class="trip-sidebar__title">{{ t('trips.title') }}</h2>
    </div>

    <div v-if="loading" class="trip-list">
      <div v-for="i in skeletonRows" :key="i" class="trip-list__item">
        <span class="trip-list__dot skeleton" />
        <div class="trip-list__info">
          <div class="trip-list__header">
            <span class="skeleton skeleton--text skeleton--text-lg" />
            <span class="skeleton skeleton--trip-time" />
          </div>
          <span class="skeleton skeleton--text skeleton--text-md" />
        </div>
      </div>
    </div>

    <div v-else-if="empty" class="empty-state text-sm">
      {{ t('trips.noTrips') }}
    </div>

    <ul v-else class="trip-list">
      <li
        v-for="{ row, realIdx } in rows"
        :key="realIdx"
        class="trip-list__item"
        :class="{ 'trip-list__item--active': selectedIndex === realIdx }"
        @click="$emit('select', realIdx)"
      >
        <span
          class="trip-list__dot"
          :class="[tripColorClass(realIdx), { 'trip-list__dot--live': realIdx === activeIndex }]"
        />
        <div class="trip-list__info">
          <div class="trip-list__header">
            <span
              v-if="row.mode === 'route'"
              class="trip-list__name trip-list__route"
              :title="row.routeTitle"
            >
              <span class="trip-list__place">{{ row.from }}</span>
              <template v-if="row.to">
                <font-awesome-icon icon="arrow-right" class="trip-list__route-arrow" />
                <span class="trip-list__place">{{ row.to }}</span>
              </template>
            </span>
            <span
              v-else-if="row.mode === 'pending'"
              class="skeleton skeleton--text skeleton--text-lg trip-list__name-skeleton"
              role="status"
              :aria-label="t('trips.resolvingPlaces')"
            />
            <span v-else class="trip-list__name" :title="row.dateLabel">{{ row.dateLabel }}</span>
            <span v-if="realIdx === activeIndex" class="trip-list__live-badge">{{
              t('trips.inProgress')
            }}</span>
            <span v-else class="trip-list__time">{{ row.timeLabel }}</span>
          </div>
          <span class="trip-list__meta" :title="row.metaTitle">{{ row.meta }}</span>
        </div>
      </li>
    </ul>
    <div ref="sentinel" />
  </aside>
</template>

<style scoped>
.trip-list__dot--live {
  animation: trip-start-pulse 2s ease-in-out infinite;
}

.trip-list__live-badge {
  font-size: 0.75rem;
  font-weight: 600;
  color: var(--color-success, #10b981);

  /* Never squeezed by a long route headline beside it; the city names truncate instead. */
  flex-shrink: 0;
  white-space: nowrap;
}
</style>
