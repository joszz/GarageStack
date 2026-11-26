<script setup lang="ts">
import { nextTick, shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRefresh } from '@/composables/useRefresh'
import { useModal } from '@/composables/useModal'
import { useUpdateNotice } from '@/composables/useUpdateNotice'
import { useMediaQuery } from '@/composables/useMediaQuery'
import { PHONE_MEDIA_QUERY } from '@/utils/viewport'
import { APP_VERSION } from '@/utils/appVersion'
import type SettingsModalComponent from './settings/SettingsModal.vue'

const { t } = useI18n()
// Without the server no command reaches the car: Refresh is hidden meanwhile.
const { canRefresh, refreshing, disabled: refreshDisabled, refresh } = useRefresh()
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

// A phone hides the footer and gives its row to the page: Settings goes to the top bar, and the
// copyright and update notice to the bottom of the menu (both targets are in App.vue). Refresh
// stays behind, as the menu's "Fetched" line refreshes on a phone.
const isPhone = useMediaQuery(PHONE_MEDIA_QUERY)
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
      <button
        v-if="canRefresh"
        class="app-footer__btn"
        :aria-label="t('control.refresh')"
        :disabled="refreshDisabled"
        @click="refresh"
      >
        <font-awesome-icon icon="rotate" :spin="refreshing" />
        <span class="app-footer__btn-label">{{ t('control.refresh') }}</span>
      </button>
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
