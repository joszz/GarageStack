<script setup lang="ts">
import '@/assets/chargeHistory.css'
import { computed, onMounted, ref, watch } from 'vue'
import { storeToRefs } from 'pinia'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { useVehicleStore } from '@/stores/vehicle'
import { useUiSettingsStore } from '@/stores/settingsUi'
import { vehicleApi, type ChargeSession } from '@/services/vehicleApi'
import { mayPlugIn } from '@/utils/vehicleType'
import { energyGainedKwh, type ChargeEnergyContext } from '@/utils/chargeSessions'
import {
  CHARGE_HISTORY_PERIOD,
  formatPeriodRange,
  periodQuery,
  resolvePeriod,
} from '@/utils/period'
import { formatNumber } from '@/utils/format'
import { formatDurationMs } from '@/utils/tripRows'
import { useErrorMessage } from '@/composables/useErrorMessage'
import { useReconnect } from '@/composables/useReconnect'
import ViewHeader from '@/components/ViewHeader.vue'
import StatusCard from '@/components/StatusCard.vue'
import CardInfoWrap from '@/components/CardInfoWrap.vue'
import ChargeSessionItem from '@/components/ChargeSessionItem.vue'
import PeriodFilterPanel from '@/components/PeriodFilterPanel.vue'

const { t } = useI18n()
const router = useRouter()
const vehicleStore = useVehicleStore()
const errorMessage = useErrorMessage()
const sessions = ref<ChargeSession[]>([])
const loading = ref(false)
const loadError = ref<unknown>(null)
// Kept in the settings, as the map's period is, so the page comes back on the one it was left on.
const { chargeHistoryPeriod } = storeToRefs(useUiSettingsStore())
const period = computed(() =>
  resolvePeriod(chargeHistoryPeriod.value, new Date(), CHARGE_HISTORY_PERIOD.maxDays),
)

const vin = computed(() => vehicleStore.activeVin)
const energyContext = computed((): ChargeEnergyContext => ({
  vehicleType: vehicleStore.effectiveVehicleType,
  configuredCapacityKwh: vehicleStore.hvBatteryCapacityKwh,
}))

// The period's totals, as the trip log totals its trips. The energy only where the car reports kWh
// worth adding up, the time only over sessions that have ended.
const totalEnergy = computed(() => {
  const known = sessions.value
    .map((session) => energyGainedKwh(session, energyContext.value))
    .filter((kwh): kwh is number => kwh !== null)
  return known.length > 0 ? known.reduce((sum, kwh) => sum + kwh, 0) : null
})

const totalTime = computed(() => {
  const ms = sessions.value.reduce(
    (sum, { startedAt, endedAt }) =>
      endedAt ? sum + new Date(endedAt).getTime() - new Date(startedAt).getTime() : sum,
    0,
  )
  return ms > 0 ? formatDurationMs(ms, t) : null
})

// A plain hybrid never charges from a plug. The menu leaves the page out for one; someone who
// opens its address anyway lands on the dashboard.
const charges = computed(() => mayPlugIn(vehicleStore.effectiveVehicleType))

async function load(): Promise<void> {
  loading.value = true
  loadError.value = null
  try {
    await vehicleStore.fetchVehicles()
    if (vehicleStore.vehiclesError) throw vehicleStore.vehiclesError
    if (!charges.value) {
      await router.replace({ name: 'dashboard' })
      return
    }
    // A period ending today asks up to now, so a session still charging is listed.
    const { fromIso, toIso } = periodQuery(period.value)
    sessions.value = vin.value
      ? await vehicleApi.chargeSessions(vin.value, fromIso, toIso ?? new Date().toISOString())
      : []
  } catch (error) {
    loadError.value = error
  } finally {
    loading.value = false
  }
}

onMounted(load)
// Back from offline: the server's sessions replace the saved ones.
useReconnect(load)
watch(() => `${period.value.first.getTime()}-${period.value.last.getTime()}`, load)
// The first load sets the active car itself; only switching to another one loads again.
watch(vin, (now, before) => {
  if (before) void load()
})
watch(charges, (now) => {
  if (!now) void router.replace({ name: 'dashboard' })
})
</script>

<template>
  <div class="view-container">
    <ViewHeader
      :title="t('chargeHistory.title')"
      :caption="formatPeriodRange(period)"
      :subtitle="t('chargeHistory.subtitle')"
    >
      <template #actions>
        <PeriodFilterPanel
          v-model="chargeHistoryPeriod"
          :options="CHARGE_HISTORY_PERIOD"
          :desc="t('chargeHistory.periodDesc')"
        />
      </template>
    </ViewHeader>

    <div v-if="sessions.length > 0" class="status-grid charge-history-totals">
      <CardInfoWrap
        :title="t('chargeHistory.sessions')"
        :description="t('chargeHistory.cardDesc.sessions')"
      >
        <StatusCard
          icon="plug-circle-bolt"
          :label="t('chargeHistory.sessions')"
          :value="sessions.length"
        />
      </CardInfoWrap>
      <CardInfoWrap
        v-if="totalEnergy !== null"
        :title="t('chargeHistory.batteryEnergyGained')"
        :description="t('chargeHistory.cardDesc.batteryEnergyGained')"
      >
        <StatusCard
          icon="bolt"
          :label="t('chargeHistory.batteryEnergyGained')"
          :value="formatNumber(totalEnergy, 1)"
          unit="kWh"
        />
      </CardInfoWrap>
      <CardInfoWrap
        v-if="totalTime"
        :title="t('chargeHistory.timeCharging')"
        :description="t('chargeHistory.cardDesc.timeCharging')"
      >
        <StatusCard icon="clock" :label="t('chargeHistory.timeCharging')" :value="totalTime" />
      </CardInfoWrap>
    </div>

    <div v-if="loadError" class="empty-state text-danger" role="alert">
      <p>{{ errorMessage(loadError) }}</p>
      <button
        type="button"
        class="btn btn-outline-secondary btn-sm"
        :disabled="loading"
        @click="load"
      >
        {{ t('common.retry') }}
      </button>
    </div>
    <div v-else-if="loading && sessions.length === 0" class="empty-state" aria-live="polite">
      {{ t('common.loading') }}
    </div>
    <div v-else-if="sessions.length === 0" class="empty-state">
      {{ t('chargeHistory.empty') }}
    </div>
    <ol v-else-if="vin" class="charge-history-list">
      <ChargeSessionItem
        v-for="(session, index) in sessions"
        :key="session.startedAt"
        :vin="vin"
        :session="session"
        :previous="sessions[index + 1] ?? null"
        :energy-context="energyContext"
      />
    </ol>
  </div>
</template>
