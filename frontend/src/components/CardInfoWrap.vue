<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useSettingsStore } from '@/stores/settings'

defineProps<{
  title: string
  description?: string
}>()

const { t } = useI18n()
const showInfo = ref(false)
const settings = useSettingsStore()
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

    <Teleport to="body">
      <Transition name="modal">
        <div v-if="showInfo" class="detail-modal-backdrop" @click.self="showInfo = false">
          <div class="detail-modal">
            <div class="detail-modal__header">
              <span class="detail-modal__title">{{ title }}</span>
              <button class="detail-modal__close" @click="showInfo = false">
                <font-awesome-icon icon="xmark" />
              </button>
            </div>
            <slot name="info">
              <p class="card-info-desc">{{ description }}</p>
            </slot>
          </div>
        </div>
      </Transition>
    </Teleport>
  </div>
</template>
