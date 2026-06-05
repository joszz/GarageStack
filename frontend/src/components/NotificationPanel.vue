<script setup lang="ts">
import { onMounted, onUnmounted, ref, computed, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import type { AppNotification } from '@/services/api'
import AppPaginator from '@/components/AppPaginator.vue'

const PAGE_SIZE = 10

const CATEGORY_ICONS: Record<string, string[]> = {
  'low-tyre': ['fas', 'circle-exclamation'],
  'low-ev': ['fas', 'battery-quarter'],
  'engine-start': ['fas', 'car'],
  'unlocked-parked': ['fas', 'lock-open'],
  'doors-open-parked': ['fas', 'door-open'],
  'windows-open-parked': ['fas', 'wind'],
}

function categoryIcon(category: string | null): string[] {
  return (category && CATEGORY_ICONS[category]) || ['fas', 'bell']
}

const props = defineProps<{
  open: boolean
  notifications: AppNotification[]
  loading: boolean
}>()

const emit = defineEmits<{
  (e: 'close'): void
  (e: 'archive', id: number): void
  (e: 'delete', id: number): void
  (e: 'archiveAll'): void
  (e: 'deleteAll'): void
}>()

const { t } = useI18n()

const page = ref(1)
const showArchived = ref(false)
const pendingAction = ref<'archiveAll' | 'deleteAll' | null>(null)

const hasUnarchived = computed(() => props.notifications.some((n) => !n.isArchived))
const hasArchived = computed(() => props.notifications.some((n) => n.isArchived))

function requestBulkAction(action: 'archiveAll' | 'deleteAll') {
  pendingAction.value = action
}

function confirmBulkAction() {
  if (pendingAction.value === 'archiveAll') emit('archiveAll')
  else if (pendingAction.value === 'deleteAll') emit('deleteAll')
  pendingAction.value = null
}

function cancelBulkAction() {
  pendingAction.value = null
}

const filteredNotifications = computed(() =>
  props.notifications.filter((n) => n.isArchived === showArchived.value),
)
const totalPages = computed(() =>
  Math.max(1, Math.ceil(filteredNotifications.value.length / PAGE_SIZE)),
)
const pagedNotifications = computed(() =>
  filteredNotifications.value.slice((page.value - 1) * PAGE_SIZE, page.value * PAGE_SIZE),
)

watch(
  () => props.notifications.length,
  () => {
    page.value = 1
  },
)
watch(
  () => props.open,
  (v) => {
    if (v) {
      page.value = 1
      showArchived.value = false
    }
  },
)
watch(showArchived, () => {
  page.value = 1
})

function formatTime(iso: string) {
  const d = new Date(iso)
  const now = new Date()
  const diff = now.getTime() - d.getTime()
  const mins = Math.floor(diff / 60000)
  if (mins < 1) return t('notifications.justNow')
  if (mins < 60) return t('notifications.minutesAgo', { n: mins })
  const hrs = Math.floor(mins / 60)
  if (hrs < 24) return t('notifications.hoursAgo', { n: hrs })
  return d.toLocaleDateString()
}

function onKey(e: KeyboardEvent) {
  if (e.key === 'Escape' && props.open) emit('close')
}
onMounted(() => document.addEventListener('keydown', onKey))
onUnmounted(() => document.removeEventListener('keydown', onKey))
</script>

<template>
  <Teleport to="body">
    <Transition name="notif-panel">
      <div v-if="open" class="notif-backdrop" @click.self="emit('close')">
        <div class="notif-panel" role="dialog" :aria-label="t('notifications.title')">
          <div class="notif-panel__header">
            <h3 class="notif-panel__title">
              <font-awesome-icon icon="bell" class="me-2" />
              {{ t('notifications.title') }}
            </h3>
            <button
              class="notif-panel__close"
              :aria-label="t('common.cancel')"
              @click="emit('close')"
            >
              <font-awesome-icon icon="xmark" />
            </button>
          </div>

          <div v-if="!loading && notifications.length > 0" class="notif-panel__tabs">
            <button
              class="notif-panel__tab"
              :class="{ 'notif-panel__tab--active': !showArchived }"
              @click="showArchived = false"
            >
              {{ t('notifications.active') }}
            </button>
            <button
              class="notif-panel__tab"
              :class="{ 'notif-panel__tab--active': showArchived }"
              @click="showArchived = true"
            >
              <font-awesome-icon icon="box-archive" class="me-1" />
              {{ t('notifications.archived') }}
            </button>
          </div>

          <div class="notif-panel__body">
            <div v-if="loading" class="notif-panel__empty text-muted">
              <font-awesome-icon icon="spinner" spin />
              {{ t('common.loading') }}
            </div>

            <div
              v-else-if="filteredNotifications.length === 0"
              class="notif-panel__empty text-muted"
            >
              <font-awesome-icon icon="bell-slash" class="notif-panel__empty-icon" />
              <span>{{
                showArchived ? t('notifications.emptyArchived') : t('notifications.empty')
              }}</span>
            </div>

            <ul v-else class="notif-panel__list">
              <li
                v-for="n in pagedNotifications"
                :key="n.id"
                class="notif-item"
                :class="{ 'notif-item--archived': n.isArchived }"
              >
                <div class="notif-item__icon">
                  <font-awesome-icon :icon="categoryIcon(n.category)" />
                </div>
                <div class="notif-item__content">
                  <span class="notif-item__title">{{ n.title }}</span>
                  <span class="notif-item__body">{{ n.body }}</span>
                  <span class="notif-item__time text-muted">{{ formatTime(n.createdAt) }}</span>
                </div>
                <div class="notif-item__actions">
                  <button
                    v-if="!n.isArchived"
                    class="notif-item__btn"
                    :title="t('notifications.archive')"
                    @click="emit('archive', n.id)"
                  >
                    <font-awesome-icon icon="box-archive" />
                  </button>
                  <button
                    class="notif-item__btn notif-item__btn--danger"
                    :title="t('notifications.delete')"
                    @click="emit('delete', n.id)"
                  >
                    <font-awesome-icon icon="trash" />
                  </button>
                </div>
              </li>
            </ul>
          </div>

          <div v-if="!loading && notifications.length > 0" class="notif-panel__footer">
            <template v-if="pendingAction">
              <div class="notif-panel__confirm">
                <span class="notif-panel__confirm-msg">
                  {{
                    pendingAction === 'archiveAll'
                      ? t('notifications.confirmArchiveAll')
                      : t('notifications.confirmDeleteAll')
                  }}
                </span>
                <div class="notif-panel__confirm-actions">
                  <button class="notif-panel__bulk-btn" @click="cancelBulkAction">
                    {{ t('common.cancel') }}
                  </button>
                  <button
                    class="notif-panel__bulk-btn"
                    :class="{ 'notif-panel__bulk-btn--danger': pendingAction === 'deleteAll' }"
                    @click="confirmBulkAction"
                  >
                    {{ t('common.confirm') }}
                  </button>
                </div>
              </div>
            </template>

            <template v-else>
              <div class="notif-panel__footer-side">
                <button
                  v-if="!showArchived && hasUnarchived"
                  class="notif-panel__bulk-btn"
                  @click="requestBulkAction('archiveAll')"
                >
                  <font-awesome-icon icon="box-archive" class="me-1" />
                  {{ t('notifications.archiveAll') }}
                </button>
              </div>

              <div class="notif-panel__footer-center">
                <AppPaginator v-if="totalPages > 1" v-model="page" :total-pages="totalPages" />
              </div>

              <div class="notif-panel__footer-side notif-panel__footer-side--right">
                <button
                  class="notif-panel__bulk-btn notif-panel__bulk-btn--danger"
                  @click="requestBulkAction('deleteAll')"
                >
                  <font-awesome-icon icon="trash" class="me-1" />
                  {{ t('notifications.deleteAll') }}
                </button>
              </div>
            </template>
          </div>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>
