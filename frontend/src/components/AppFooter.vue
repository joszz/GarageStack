<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useVehicleStore } from '@/stores/vehicle'
import { useVehicleCommand } from '@/composables/useVehicleCommand'
import { useModal } from '@/composables/useModal'
import SettingsModal from './settings/SettingsModal.vue'
import { APP_VERSION } from '@/utils/appVersion'

const { t } = useI18n()
const vehicleStore = useVehicleStore()
const { sending, send } = useVehicleCommand()
const { isOpen: modalOpen, open: openModal, close: closeModal } = useModal()

// Joined in script rather than the template, which drops a whitespace-only text node before the version
const copyrightLabel = ['© 2026 GarageStack', APP_VERSION].filter(Boolean).join(' ')

const vin = computed(() => vehicleStore.activeVin)

function refresh() {
  if (!vin.value) return
  send(vin.value, 'refresh', 'force')
}
</script>

<template>
  <footer class="app-footer">
    <a
      class="app-footer__copyright"
      href="https://github.com/joszz/GarageStack"
      target="_blank"
      rel="noopener"
      >{{ copyrightLabel }}</a
    >
    <div class="app-footer__actions">
      <button
        class="app-footer__btn"
        :aria-label="t('control.refresh')"
        :disabled="vehicleStore.loading || sending === 'refresh' || !vin"
        @click="refresh"
      >
        <font-awesome-icon icon="rotate" :spin="vehicleStore.loading || sending === 'refresh'" />
        {{ t('control.refresh') }}
      </button>
      <button class="app-footer__btn" :aria-label="t('settings.title')" @click="openModal">
        <font-awesome-icon icon="gear" />
        {{ t('settings.title') }}
      </button>
    </div>
  </footer>

  <SettingsModal :open="modalOpen" @close="closeModal" />
</template>
