<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import gbFlag from 'flag-icons/flags/4x3/gb.svg'
import nlFlag from 'flag-icons/flags/4x3/nl.svg'
import { useUiSettingsStore } from '@/stores/settingsUi'
import DetailModal from '../DetailModal.vue'
import SettingsToggle from '../SettingsToggle.vue'
import UnitSettings from '../UnitSettings.vue'
import SettingsSection from './SettingsSection.vue'
import SideIconSwitch from './SideIconSwitch.vue'
import CarColorPicker from './CarColorPicker.vue'
import VehicleTypeSetting from './VehicleTypeSetting.vue'
import NotificationTypeSettings from './NotificationTypeSettings.vue'

defineProps<{ open: boolean }>()
defineEmits<{ close: [] }>()

const { t } = useI18n()
const settings = useUiSettingsStore()

const isLightTheme = computed({
  get: () => settings.theme === 'light',
  set: (val: boolean) => {
    settings.theme = val ? 'light' : 'dark'
  },
})

const isNL = computed({
  get: () => settings.locale === 'nl',
  set: (val: boolean) => {
    settings.locale = val ? 'nl' : 'en'
  },
})
</script>

<template>
  <DetailModal :open="open" :title="t('settings.title')" @close="$emit('close')">
    <SettingsSection :title="t('settings.theme.title')">
      <div class="settings-toggles">
        <SideIconSwitch
          v-model="isLightTheme"
          :label="isLightTheme ? t('settings.theme.light') : t('settings.theme.dark')"
          :desc="t('settings.theme.lightDesc')"
          input-id="footer-toggle-theme"
        >
          <template #off><font-awesome-icon icon="moon" /></template>
          <template #on><font-awesome-icon icon="sun" /></template>
        </SideIconSwitch>
      </div>
    </SettingsSection>

    <SettingsSection :title="t('settings.dashboard.title')">
      <div class="settings-toggles">
        <SettingsToggle
          v-model="settings.showCardInfoIcons"
          :label="t('settings.dashboard.cardInfoIcons')"
          :desc="t('settings.dashboard.cardInfoIconsDesc')"
          input-id="footer-toggle-card-info-icons"
        />
      </div>
    </SettingsSection>

    <SettingsSection :title="t('settings.map.title')">
      <div class="settings-toggles">
        <SettingsToggle
          v-model="settings.placeNamesEnabled"
          :label="t('settings.map.placeNames')"
          :desc="t('settings.map.placeNamesDesc')"
          input-id="footer-toggle-place-names"
        />
      </div>
    </SettingsSection>

    <SettingsSection :title="t('settings.language.title')">
      <div class="settings-toggles">
        <SideIconSwitch
          v-model="isNL"
          :label="isNL ? 'Nederlands' : 'English'"
          input-id="footer-toggle-lang"
          flags
        >
          <template #off>
            <img :src="gbFlag" alt="English" class="settings-toggle__flag" />
          </template>
          <template #on>
            <img :src="nlFlag" alt="Nederlands" class="settings-toggle__flag" />
          </template>
        </SideIconSwitch>
      </div>
    </SettingsSection>

    <SettingsSection :title="t('settings.units.title')">
      <UnitSettings />
    </SettingsSection>

    <SettingsSection :title="t('settings.carColor.title')">
      <CarColorPicker />
    </SettingsSection>

    <SettingsSection :title="t('settings.vehicleType.title')">
      <VehicleTypeSetting />
    </SettingsSection>

    <SettingsSection :title="t('settings.notificationTypes.title')">
      <NotificationTypeSettings />
    </SettingsSection>
  </DetailModal>
</template>
