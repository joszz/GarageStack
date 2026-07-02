<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import StatusCard from './StatusCard.vue'
import DetailModal from './DetailModal.vue'
import DetailListItem from './DetailListItem.vue'
import { useModal } from '@/composables/useModal'

const { t } = useI18n()

const props = defineProps<{
  mainBeam: boolean | null
  dippedBeam: boolean | null
  side: boolean | null
}>()

const { isOpen: modalOpen, open: openModal, close: closeModal } = useModal()

type LightItem = { key: string; label: string; on: boolean }

const lightList = computed((): LightItem[] =>
  (
    [
      { key: 'main', label: t('vehicle.lights.mainBeam'), on: props.mainBeam },
      { key: 'dipped', label: t('vehicle.lights.dippedBeam'), on: props.dippedBeam },
      { key: 'side', label: t('vehicle.lights.side'), on: props.side },
    ] as { key: string; label: string; on: boolean | null }[]
  ).filter((l): l is LightItem => l.on !== null),
)

const activeLights = computed(() => lightList.value.filter((l) => l.on))

const summary = computed((): string | null => {
  if (lightList.value.length === 0) return null
  if (activeLights.value.length === 0) return t('common.off')
  return activeLights.value.map((l) => l.label).join(' · ')
})
</script>

<template>
  <StatusCard
    v-if="lightList.length > 0"
    icon="lightbulb"
    :label="t('vehicle.lights.title')"
    :value="summary"
    :variant="activeLights.length > 0 ? 'success' : 'danger'"
    clickable
    @click="openModal"
  />

  <DetailModal :open="modalOpen" :title="t('vehicle.lights.title')" @close="closeModal">
    <div class="detail-list">
      <DetailListItem
        v-for="light in lightList"
        :key="light.key"
        icon="lightbulb"
        :label="light.label"
        :alert="light.on"
      >
        <template #value>
          <span class="badge" :class="light.on ? 'badge-warning' : 'badge-secondary'">
            {{ light.on ? t('common.on') : t('common.off') }}
          </span>
        </template>
      </DetailListItem>
    </div>
  </DetailModal>
</template>
