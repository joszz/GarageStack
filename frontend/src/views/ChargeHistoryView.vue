<script setup lang="ts">
import '@/assets/chargeHistory.css'
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useVehicleStore } from '@/stores/vehicle'
import { vehicleApi, type ChargeSession } from '@/services/vehicleApi'
import { formatDateTime, formatNumber } from '@/utils/format'
import { formatDurationMs } from '@/utils/tripRows'
import { useErrorMessage } from '@/composables/useErrorMessage'
import ViewHeader from '@/components/ViewHeader.vue'
import StatusCard from '@/components/StatusCard.vue'

const { t } = useI18n()
const vehicleStore = useVehicleStore()
const errorMessage = useErrorMessage()
const sessions = ref<ChargeSession[]>([])
const loading = ref(false)
const loadError = ref<unknown>(null)

const vin = computed(() => vehicleStore.activeVin)
const sessionCount = computed(() => sessions.value.length)

async function load() {
  loading.value = true
  loadError.value = null
  try {
    await vehicleStore.fetchVehicles()
    if (vehicleStore.vehiclesError) throw vehicleStore.vehiclesError
    sessions.value = vin.value ? await vehicleApi.chargeSessions(vin.value) : []
  } catch (error) {
    loadError.value = error
  } finally {
    loading.value = false
  }
}

onMounted(load)

function duration(session: ChargeSession): string {
  if (!session.endedAt) return t('chargeHistory.ongoing')
  return formatDurationMs(
    new Date(session.endedAt).getTime() - new Date(session.startedAt).getTime(),
    t,
  )
}

function stateOfCharge(session: ChargeSession): string {
  const start =
    session.startSocPercent === null
      ? t('chargeHistory.notReported')
      : `${formatNumber(session.startSocPercent, 0)}%`
  const end =
    session.endSocPercent === null
      ? t('chargeHistory.notReported')
      : `${formatNumber(session.endSocPercent, 0)}%`
  return `${start} → ${end}`
}
</script>

<template>
  <div class="view-container">
    <ViewHeader :title="t('chargeHistory.title')" :subtitle="t('chargeHistory.subtitle')" />

    <div v-if="sessionCount > 0" class="charge-history-summary">
      <StatusCard
        icon="plug-circle-bolt"
        :label="t('chargeHistory.sessions')"
        :value="sessionCount"
        variant="info"
      />
    </div>

    <div v-if="loadError" class="empty-state text-danger" role="alert">
      <p>{{ errorMessage(loadError) }}</p>
      <button class="btn btn-outline-secondary btn-sm" :disabled="loading" @click="load">
        {{ t('common.retry') }}
      </button>
    </div>
    <div v-else-if="loading && sessions.length === 0" class="empty-state" aria-live="polite">
      {{ t('common.loading') }}
    </div>
    <div v-else-if="sessions.length === 0" class="empty-state">
      {{ t('chargeHistory.empty') }}
    </div>
    <ol v-else class="charge-history-list">
      <li
        v-for="(session, index) in sessions"
        :key="`${session.startedAt}-${index}`"
        class="charge-history-item"
      >
        <div class="charge-history-item__heading">
          <h2>{{ formatDateTime(session.startedAt) }}</h2>
          <span
            class="charge-history-item__status"
            :class="{ 'charge-history-item__status--active': !session.endedAt }"
          >
            {{ session.endedAt ? t('chargeHistory.completed') : t('chargeHistory.ongoing') }}
          </span>
        </div>
        <dl class="charge-history-item__details">
          <div>
            <dt>{{ t('chargeHistory.duration') }}</dt>
            <dd>{{ duration(session) }}</dd>
          </div>
          <div>
            <dt>{{ t('chargeHistory.stateOfCharge') }}</dt>
            <dd>{{ stateOfCharge(session) }}</dd>
          </div>
          <div v-if="session.chargingType">
            <dt>{{ t('chargeHistory.type') }}</dt>
            <dd>{{ session.chargingType }}</dd>
          </div>
          <div v-if="session.endedAt">
            <dt>{{ t('chargeHistory.ended') }}</dt>
            <dd>{{ formatDateTime(session.endedAt) }}</dd>
          </div>
        </dl>
      </li>
    </ol>
  </div>
</template>
