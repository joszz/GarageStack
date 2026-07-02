<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useUiSettingsStore } from '@/stores/settingsUi'
import DetailModal from './DetailModal.vue'

defineProps<{
  title: string
  description?: string
}>()

const { t } = useI18n()
const showInfo = ref(false)
const settings = useUiSettingsStore()
</script>

<template>
  <div class="card-info-wrap">
    <slot />
    <button
      v-if="settings.showCardInfoIcons"
      class="card-info-btn"
      :aria-label="t('dashboard.cardInfoBtn')"
      @click.stop="showInfo = true"
    >
      <font-awesome-icon icon="circle-info" />
    </button>

    <DetailModal :open="showInfo" :title="title" @close="showInfo = false">
      <slot name="info">
        <p class="card-info-desc">{{ description }}</p>
      </slot>
    </DetailModal>
  </div>
</template>
