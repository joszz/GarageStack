<script setup lang="ts">
import { computed, nextTick, shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useVehicleStore } from '@/stores/vehicle'
import { useVehicleCommand } from '@/composables/useVehicleCommand'
import { useConnection } from '@/composables/useConnection'
import { useModal } from '@/composables/useModal'
import { useUpdateNotice } from '@/composables/useUpdateNotice'
import { useMediaQuery } from '@/composables/useMediaQuery'
import { PHONE_MEDIA_QUERY } from '@/utils/viewport'
import { APP_VERSION } from '@/utils/appVersion'
import type SettingsModalComponent from './settings/SettingsModal.vue'

const { t } = useI18n()
const vehicleStore = useVehicleStore()
const { sending, send } = useVehicleCommand()
// Without the server no command reaches the car: the controls are hidden meanwhile.
const connection = useConnection()
const canSend = computed(() => connection.reachable)
const { isOpen: modalOpen, open: showModal, close: closeModal } = useModal()
const { notice: updateNotice } = useUpdateNotice()

// The settings modal used to ship with the footer on every page, for a dialog most visits never
// open. It loads on first use instead.
const settingsModal = shallowRef<typeof SettingsModalComponent | null>(null)

async function openModal() {
  if (!settingsModal.value) {
    settingsModal.value = (await import('./settings/SettingsModal.vue')).default
    // Mount it closed first: its transition only plays for a change of `open`, not for a
    // dialog that arrives already open.
    await nextTick()
  }
  showModal()
}

// Joined in script rather than the template, which drops a whitespace-only text node before the version
const copyrightLabel = ['© 2026 GarageStack', APP_VERSION].filter(Boolean).join(' ')

const vin = computed(() => vehicleStore.activeVin)

// A phone hides the footer and gives its row to the page: Settings goes to the top bar, Refresh to
// the menu beside the time it renews, and the copyright and update notice to the bottom of the
// menu (every target is in App.vue).
const isPhone = useMediaQuery(PHONE_MEDIA_QUERY)

function refresh() {
  if (!vin.value) return
  send(vin.value, 'refresh', 'force')
}
</script>

<template>
  <footer class="app-footer">
    <Teleport defer to="#sidebar-meta" :disabled="!isPhone">
      <div class="app-footer__meta">
        <a
          class="app-footer__copyright"
          href="https://github.com/joszz/GarageStack"
          target="_blank"
          rel="noopener"
          >{{ copyrightLabel }}</a
        >
        <a
          v-if="updateNotice"
          class="app-footer__update"
          :href="updateNotice.releaseUrl"
          target="_blank"
          rel="noopener noreferrer"
          :aria-label="t('updates.availableLabel', { version: updateNotice.version })"
          :title="t('updates.availableLabel', { version: updateNotice.version })"
        >
          <font-awesome-icon icon="circle-up" />
          <span class="app-footer__update-text">{{
            t('updates.available', { version: updateNotice.version })
          }}</span>
        </a>
      </div>
    </Teleport>
    <div class="app-footer__actions">
      <Teleport defer to="#sidebar-refresh" :disabled="!isPhone">
        <button
          v-if="canSend"
          class="app-footer__btn"
          :aria-label="t('control.refresh')"
          :title="t('control.refresh')"
          :disabled="vehicleStore.loading || sending === 'refresh' || !vin"
          @click="refresh"
        >
          <font-awesome-icon icon="rotate" :spin="vehicleStore.loading || sending === 'refresh'" />
          <span class="app-footer__btn-label">{{ t('control.refresh') }}</span>
        </button>
      </Teleport>
      <Teleport defer to="#mobile-topbar-actions" :disabled="!isPhone">
        <button class="app-footer__btn" :aria-label="t('settings.title')" @click="openModal">
          <font-awesome-icon icon="gear" />
          <span class="app-footer__btn-label">{{ t('settings.title') }}</span>
        </button>
      </Teleport>
    </div>
  </footer>

  <component :is="settingsModal" v-if="settingsModal" :open="modalOpen" @close="closeModal" />
</template>
