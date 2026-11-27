<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { useUiSettingsStore } from '@/stores/settingsUi'
import { useKeepScreenOnOffered } from '@/composables/useScreenWakeLock'
import { usePhoneGpsOffered, usePhoneGpsStatus } from '@/composables/usePhoneGpsRecorder'
import { useEdgeSwipeOffered } from '@/composables/useDrawerSwipe'
import SettingsToggle from '../SettingsToggle.vue'
import SettingsSection from './SettingsSection.vue'

const { t } = useI18n()
const settings = useUiSettingsStore()
const screenOffered = useKeepScreenOnOffered()
const gpsOffered = usePhoneGpsOffered()
const edgeSwipeOffered = useEdgeSwipeOffered()
const { denied, recording } = usePhoneGpsStatus()
</script>

<template>
  <!-- What suits one device. Keeping the screen on and the phone's GPS are for the installed app: a
       browser tab dims like any other page, and only an app kept on screen can follow the phone's
       GPS. Swiping the menu open is for any phone with a touchscreen. All stay with this device. -->
  <SettingsSection v-if="screenOffered || edgeSwipeOffered" :title="t('settings.device.title')">
    <div class="settings-toggles">
      <SettingsToggle
        v-if="screenOffered"
        v-model="settings.keepScreenOn"
        :label="t('settings.keepScreenOn.label')"
        :desc="t('settings.keepScreenOn.desc')"
        input-id="footer-toggle-keep-screen-on"
      />
      <SettingsToggle
        v-if="screenOffered && gpsOffered"
        v-model="settings.recordPhoneGps"
        :label="t('settings.phoneGps.label')"
        :desc="t('settings.phoneGps.desc')"
        :disabled="!settings.keepScreenOn"
        input-id="footer-toggle-record-phone-gps"
      >
        <template #note>
          <p v-if="!settings.keepScreenOn" class="text-muted text-sm">
            {{ t('settings.phoneGps.needsScreen') }}
          </p>
          <p v-else-if="denied" class="text-danger text-sm" role="alert">
            {{ t('settings.phoneGps.denied') }}
          </p>
          <p v-else-if="recording" class="text-muted text-sm" role="status">
            {{ t('settings.phoneGps.recording') }}
          </p>
        </template>
      </SettingsToggle>
      <SettingsToggle
        v-if="edgeSwipeOffered"
        v-model="settings.edgeSwipeMenu"
        :label="t('settings.edgeSwipeMenu.label')"
        :desc="t('settings.edgeSwipeMenu.desc')"
        input-id="footer-toggle-edge-swipe-menu"
      />
    </div>
  </SettingsSection>
</template>
