<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import StatusCard from './StatusCard.vue'
import DetailModal from './DetailModal.vue'
import DetailListItem from './DetailListItem.vue'
import { useModal } from '@/composables/useModal'

const { t } = useI18n()

const props = defineProps<{
  batteryHeating: boolean | null
  scheduleMode: string | null
  scheduleStartTime: string | null
}>()

const { isOpen: modalOpen, open: openModal, close: closeModal } = useModal()

const summaryValue = computed((): string | null => {
  if (props.batteryHeating === null) return null
  const state = props.batteryHeating ? t('common.on') : t('common.off')
  if (props.scheduleMode && props.scheduleMode !== 'off' && props.scheduleStartTime)
    return `${state} · ${props.scheduleStartTime}`
  return state
})

const hasModal = computed(() => props.scheduleMode !== null || props.scheduleStartTime !== null)
</script>

<template>
  <StatusCard
    v-if="batteryHeating !== null"
    icon="temperature-arrow-up"
    :label="t('vehicle.batteryHeating.title')"
    :value="summaryValue"
    :variant="batteryHeating ? 'info' : undefined"
    :clickable="hasModal"
    @click="hasModal ? openModal() : undefined"
  />

  <DetailModal
    v-if="hasModal"
    :open="modalOpen"
    :title="t('vehicle.batteryHeating.title')"
    @close="closeModal"
  >
    <div class="detail-list">
      <DetailListItem
        v-if="batteryHeating !== null"
        icon="temperature-arrow-up"
        :label="t('vehicle.batteryHeating.title')"
      >
        <template #value>
          <span class="badge" :class="batteryHeating ? 'badge-info' : 'badge-secondary'">
            {{ batteryHeating ? t('common.on') : t('common.off') }}
          </span>
        </template>
      </DetailListItem>
      <DetailListItem
        v-if="scheduleMode !== null"
        icon="calendar-check"
        :label="t('vehicle.batteryHeating.schedule')"
      >
        <template #value>
          <span class="badge" :class="scheduleMode !== 'off' ? 'badge-info' : 'badge-secondary'">
            {{ scheduleMode }}
          </span>
        </template>
      </DetailListItem>
      <DetailListItem
        v-if="scheduleStartTime !== null"
        icon="clock"
        :value="scheduleStartTime"
        :label="t('vehicle.batteryHeating.startTime')"
      />
    </div>
  </DetailModal>
</template>
