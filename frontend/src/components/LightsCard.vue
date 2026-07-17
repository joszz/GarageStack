<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import ExpandableStatusCard from './ExpandableStatusCard.vue'
import DetailListItem from './DetailListItem.vue'
import { useBooleanStatusList } from '@/composables/useBooleanStatusList'

const { t } = useI18n()

const props = defineProps<{
  mainBeam: boolean | null
  dippedBeam: boolean | null
  side: boolean | null
}>()

const lightList = useBooleanStatusList(() => [
  { key: 'main', label: t('vehicle.lights.mainBeam'), open: props.mainBeam },
  { key: 'dipped', label: t('vehicle.lights.dippedBeam'), open: props.dippedBeam },
  { key: 'side', label: t('vehicle.lights.side'), open: props.side },
])

const activeLights = computed(() => lightList.value.filter((l) => l.open))

const summary = computed((): string | null => {
  if (lightList.value.length === 0) return null
  if (activeLights.value.length === 0) return t('common.off')
  return activeLights.value.map((l) => l.label).join(' · ')
})
</script>

<template>
  <ExpandableStatusCard
    v-if="lightList.length > 0"
    icon="lightbulb"
    :title="t('vehicle.lights.title')"
    :value="summary"
    :variant="activeLights.length > 0 ? 'success' : 'danger'"
  >
    <div class="detail-list">
      <DetailListItem
        v-for="light in lightList"
        :key="light.key"
        icon="lightbulb"
        :label="light.label"
        :alert="light.open"
      >
        <template #value>
          <span class="badge" :class="light.open ? 'badge-warning' : 'badge-secondary'">
            {{ light.open ? t('common.on') : t('common.off') }}
          </span>
        </template>
      </DetailListItem>
    </div>
  </ExpandableStatusCard>
</template>
