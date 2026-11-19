<script setup lang="ts">
import '@/assets/chargeHistory.css'
import { computed, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { useVehicleStore } from '@/stores/vehicle'
import { vehicleApi, type ChargeSession } from '@/services/vehicleApi'
import { mayPlugIn } from '@/utils/vehicleType'
import { energyGainedKwh, type ChargeEnergyContext } from '@/utils/chargeSessions'
import { formatCurrency, formatNumber } from '@/utils/format'
import { formatDurationMs } from '@/utils/tripRows'
import {
  chargeSessionPriceKey,
  useChargeHistorySettingsStore,
} from '@/stores/chargeHistorySettings'
import { useErrorMessage } from '@/composables/useErrorMessage'
import { useReconnect } from '@/composables/useReconnect'
import ViewHeader from '@/components/ViewHeader.vue'
import StatusCard from '@/components/StatusCard.vue'
import CardInfoWrap from '@/components/CardInfoWrap.vue'
import ChargeSessionItem from '@/components/ChargeSessionItem.vue'

/** The periods offered, in days. The API reads at most a year at a time. */
const PERIOD_DAYS = [30, 90, 365] as const
type PeriodDays = (typeof PERIOD_DAYS)[number]

const { t } = useI18n()
const router = useRouter()
const vehicleStore = useVehicleStore()
const chargeHistorySettings = useChargeHistorySettingsStore()
const errorMessage = useErrorMessage()
const sessions = ref<ChargeSession[]>([])
const loading = ref(false)
const loadError = ref<unknown>(null)
const periodDays = ref<PeriodDays>(90)
const defaultRateInput = ref<string | number>('')

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

const pricedSessionCount = computed(
  () => sessions.value.filter((session) => sessionCost(session) !== null).length,
)

const totalCost = computed(() => {
  let total = 0
  let count = 0
  for (const session of sessions.value) {
    const cost = sessionCost(session)
    if (cost !== null) {
      total += cost
      count++
    }
  }
  return count > 0 ? total : null
})

function sessionCost(session: ChargeSession): number | null {
  if (!vin.value) return null
  const manual =
    chargeHistorySettings.manualSessionPrices[chargeSessionPriceKey(vin.value, session.startedAt)]
  if (manual !== undefined) return manual
  const rate = chargeHistorySettings.defaultRatePerKwh
  const energy = energyGainedKwh(session, energyContext.value)
  return rate !== null && energy !== null ? rate * energy : null
}

watch(
  () => chargeHistorySettings.defaultRatePerKwh,
  (rate) => {
    defaultRateInput.value = rate === null ? '' : String(rate)
  },
  { immediate: true },
)

function saveDefaultRate(): void {
  const value = String(defaultRateInput.value).trim()
  if (value === '') {
    chargeHistorySettings.defaultRatePerKwh = null
    return
  }
  const rate = Number(value)
  if (Number.isFinite(rate) && rate >= 0) {
    chargeHistorySettings.defaultRatePerKwh = rate
  } else {
    defaultRateInput.value =
      chargeHistorySettings.defaultRatePerKwh === null
        ? ''
        : String(chargeHistorySettings.defaultRatePerKwh)
  }
}

function updateManualPrice(startedAt: string, price: number | null): void {
  const activeVin = vin.value
  if (!activeVin) return
  if (price === null) {
    chargeHistorySettings.clearManualSessionPrice(activeVin, startedAt)
  } else {
    chargeHistorySettings.setManualSessionPrice(activeVin, startedAt, price)
  }
}

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
    const to = new Date()
    const from = new Date(to.getTime() - periodDays.value * 24 * 60 * 60 * 1000)
    sessions.value = vin.value
      ? await vehicleApi.chargeSessions(vin.value, from.toISOString(), to.toISOString())
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
watch(periodDays, load)
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
    <ViewHeader :title="t('chargeHistory.title')" :subtitle="t('chargeHistory.subtitle')" />

    <div class="charge-history-filters">
      <label class="charge-history-filters__field">
        <span class="text-xs text-muted">{{ t('chargeHistory.period') }}</span>
        <select v-model.number="periodDays" class="form-select">
          <option v-for="days in PERIOD_DAYS" :key="days" :value="days">
            {{ t('chargeHistory.lastDays', { days }) }}
          </option>
        </select>
      </label>
      <label class="charge-history-filters__field">
        <span class="text-xs text-muted">{{ t('chargeHistory.defaultRate') }}</span>
        <span class="charge-history-rate">
          <input
            v-model="defaultRateInput"
            class="form-control"
            type="number"
            min="0"
            step="0.01"
            inputmode="decimal"
            @change="saveDefaultRate"
          />
          <span aria-hidden="true">€/kWh</span>
        </span>
      </label>
    </div>

    <div v-if="sessions.length > 0" class="status-grid charge-history-totals">
      <CardInfoWrap
        :title="t('chargeHistory.costOfRefills')"
        :description="t('chargeHistory.cardDesc.costOfRefills')"
      >
        <StatusCard
          icon="euro-sign"
          :label="t('chargeHistory.costOfRefills')"
          :value="
            totalCost === null ? t('chargeHistory.setRate') : formatCurrency(totalCost, 'EUR')
          "
          :note="t('chargeHistory.pricedSessions', { count: pricedSessionCount })"
        />
      </CardInfoWrap>
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
        :manual-price="
          chargeHistorySettings.manualSessionPrices[
            chargeSessionPriceKey(vin, session.startedAt)
          ] ?? null
        "
        :default-rate-per-kwh="chargeHistorySettings.defaultRatePerKwh"
        @update-manual-price="(price) => updateManualPrice(session.startedAt, price)"
      />
    </ol>
  </div>
</template>
