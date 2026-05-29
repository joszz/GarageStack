<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import DetailModal from './DetailModal.vue'

const { t } = useI18n()
const isOpen = ref(false)
</script>

<template>
  <div>
    <button class="btn btn-sm btn-outline-secondary" @click="isOpen = true">
      <font-awesome-icon icon="sliders" />{{ t('common.filters') }}
    </button>
    <DetailModal :open="isOpen" :title="t('common.filters')" @close="isOpen = false">
      <div class="settings-toggles">
        <slot />
      </div>
    </DetailModal>
  </div>
</template>
