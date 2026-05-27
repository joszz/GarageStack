<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import type { CardId } from '@/stores/settings'

const props = defineProps<{ cardId: CardId }>()
const { t } = useI18n()

const showInfo = ref(false)
</script>

<template>
  <div class="card-info-wrap">
    <slot />
    <button
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
              <span class="detail-modal__title">{{ t(`settings.cards.${props.cardId}`) }}</span>
              <button class="detail-modal__close" @click="showInfo = false">
                <font-awesome-icon icon="xmark" />
              </button>
            </div>
            <p class="card-info-desc">{{ t(`dashboard.cardDesc.${props.cardId}`) }}</p>
          </div>
        </div>
      </Transition>
    </Teleport>
  </div>
</template>
