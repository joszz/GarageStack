<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { useUiSettingsStore } from '@/stores/settingsUi'
import { useKeepScreenOnOffered } from '@/composables/useScreenWakeLock'
import { usePhoneGpsOffered, usePhoneGpsStatus } from '@/composables/usePhoneGpsRecorder'
import SettingsToggle from '../SettingsToggle.vue'
import SettingsSection from './SettingsSection.vue'

const { t } = useI18n()
const settings = useUiSettingsStore()
const screenOffered = useKeepScreenOnOffered()
const gpsOffered = usePhoneGpsOffered()
const { denied, recording } = usePhoneGpsStatus()
</script>

<template>
  <!-- Only in the installed app: a browser tab dims like any other page, and only an app kept
       on screen can follow the phone's GPS. Both stay with this device. -->
  <SettingsSection v-if="screenOffered" :title="t('settings.device.title')">
    <div class="settings-toggles">
      <SettingsToggle
        v-model="settings.keepScreenOn"
        :label="t('settings.keepScreenOn.label')"
        :desc="t('settings.keepScreenOn.desc')"
        input-id="footer-toggle-keep-screen-on"
      />
      <SettingsToggle
        v-if="gpsOffered"
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
    </div>
  </SettingsSection>
</template>
