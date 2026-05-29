<script setup lang="ts">
import { onMounted, onUnmounted } from 'vue'
import { useI18n } from 'vue-i18n'
import type { AppNotification } from '@/services/api'

const props = defineProps<{
  open: boolean
  notifications: AppNotification[]
  loading: boolean
}>()

const emit = defineEmits<{
  (e: 'close'): void
  (e: 'archive', id: number): void
  (e: 'delete', id: number): void
}>()

const { t } = useI18n()

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
            <button class="notif-panel__close" :aria-label="t('common.cancel')" @click="emit('close')">
              <font-awesome-icon icon="xmark" />
            </button>
          </div>

          <div class="notif-panel__body">
            <div v-if="loading" class="notif-panel__empty text-muted">
              <font-awesome-icon icon="spinner" spin />
              {{ t('common.loading') }}
            </div>

            <div v-else-if="notifications.length === 0" class="notif-panel__empty text-muted">
              <font-awesome-icon icon="bell-slash" class="notif-panel__empty-icon" />
              <span>{{ t('notifications.empty') }}</span>
            </div>

            <ul v-else class="notif-panel__list">
              <li
                v-for="n in notifications"
                :key="n.id"
                class="notif-item"
                :class="{ 'notif-item--archived': n.isArchived }"
              >
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
        </div>
      </div>
    </Transition>
  </Teleport>
</template>
