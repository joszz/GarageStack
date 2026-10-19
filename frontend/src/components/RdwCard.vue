<script setup lang="ts">
import { computed, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useVehicleStore } from '@/stores/vehicle'
import { useRdwStore } from '@/stores/rdw'
import type { RdwRecall } from '@/services/rdwApi'
import { formatDate, formatDateTime } from '@/utils/format'
import { parseLocalDate } from '@/utils/statsPeriod'
import type { BadgeVariant } from '@/utils/levels'
import ExpandableStatusCard from './ExpandableStatusCard.vue'
import DetailListItem from './DetailListItem.vue'
import StatusBadge from './StatusBadge.vue'

// The Dutch registration: when the APK runs out, the open recalls and the insurance, as the RDW
// has them. The card shows the APK; the dialog shows everything, each open recall in full.
const { t } = useI18n()
const vehicleStore = useVehicleStore()
const store = useRdwStore()

const vin = computed(() => vehicleStore.activeVin)

watch(
  vin,
  (v) => {
    if (v) store.ensureLoaded(v)
  },
  { immediate: true },
)

const plate = computed(() => store.status?.licensePlate ?? null)
const info = computed(() => store.status?.info ?? null)
const known = computed(() => (info.value?.found ? info.value : null))
const openRecalls = computed(() => known.value?.recalls.filter((r) => r.open) ?? [])
const repairedRecalls = computed(() => known.value?.recalls.filter((r) => !r.open) ?? [])
const daysLeft = computed(() => known.value?.apkDaysLeft ?? null)

const value = computed(() => {
  if (!info.value) return t('rdw.notLookedUp')
  if (!info.value.found) return t('rdw.plateUnknown')
  const days = daysLeft.value
  if (days === null) return '-'
  if (days < 0) return t('rdw.expired')
  if (days === 0) return t('rdw.lastDay')
  return t('rdw.daysLeft', { n: days }, days)
})

const note = computed(() => {
  if (!known.value) return plate.value ? t('rdw.plate', { plate: plate.value }) : undefined
  return t('rdw.openRecalls', { n: openRecalls.value.length }, openRecalls.value.length)
})

// Red for what keeps the car off the road (no APK, no insurance), amber for what wants an
// appointment soon (an APK within the month, a recall to have repaired).
const variant = computed((): 'success' | 'warning' | 'danger' | undefined => {
  const k = known.value
  if (!k) return undefined
  const days = k.apkDaysLeft
  if ((days !== null && days < 0) || k.wamInsured === false) return 'danger'
  if ((days !== null && days <= 30) || openRecalls.value.length > 0) return 'warning'
  return 'success'
})

const apkDate = computed(() => {
  const date = parseLocalDate(known.value?.apkExpiresOn)
  return date ? formatDate(date, { day: 'numeric', month: 'long', year: 'numeric' }) : null
})

const apkDaysText = computed(() => {
  const days = daysLeft.value
  if (days === null) return null
  return days < 0
    ? t('rdw.apkExpiredOn', { n: -days }, -days)
    : t('rdw.apkDaysLeft', { n: days }, days)
})

const insurance = computed(() => {
  const insured = known.value?.wamInsured
  if (insured === true) return t('rdw.insured')
  if (insured === false) return t('rdw.notInsured')
  return t('rdw.insuranceUnknown')
})

// The RDW's trade name often starts with the make already ("MG EHS PLUG-IN HYBRID").
const vehicleName = computed(() => {
  const make = known.value?.make ?? ''
  const model = known.value?.model ?? ''
  if (!model) return make
  return model.toUpperCase().startsWith(make.toUpperCase()) ? model : `${make} ${model}`.trim()
})

const SEVERITY_VARIANTS: Record<string, BadgeVariant> = {
  ERN: 'danger',
  GEM: 'warning',
  NNB: 'secondary',
}

function severityVariant(recall: RdwRecall): BadgeVariant {
  return SEVERITY_VARIANTS[recall.severity ?? ''] ?? 'secondary'
}

function severityLabel(recall: RdwRecall): string | null {
  return recall.severity && recall.severity in SEVERITY_VARIANTS
    ? t(`rdw.severity.${recall.severity}`)
    : null
}

const fetchedAt = computed(() =>
  info.value?.fetchedAt ? t('rdw.fetchedAt', { time: formatDateTime(info.value.fetchedAt) }) : null,
)
</script>

<template>
  <ExpandableStatusCard
    icon="clipboard-check"
    :title="t('rdw.title')"
    :value="value"
    :note="note"
    :variant="variant"
  >
    <p v-if="info && !info.found" class="card-info-desc">
      {{ t('rdw.notFound', { plate }) }}
    </p>

    <template v-if="known">
      <div class="detail-list">
        <DetailListItem
          icon="calendar-check"
          :label="t('rdw.apkExpires')"
          :value="apkDate ? `${apkDate} · ${apkDaysText}` : t('rdw.apkUnknown')"
          :alert="daysLeft !== null && daysLeft <= 30"
        />
        <DetailListItem
          icon="shield-halved"
          :label="t('rdw.insurance')"
          :value="insurance"
          :alert="known.wamInsured === false"
        />
        <DetailListItem
          v-if="vehicleName"
          icon="car"
          :label="t('rdw.registeredAs')"
          :value="`${vehicleName} · ${plate}`"
        />
      </div>

      <div class="detail-modal__section-title mt-3">{{ t('rdw.openRecallsTitle') }}</div>
      <p v-if="openRecalls.length === 0" class="card-info-desc">{{ t('rdw.noOpenRecalls') }}</p>
      <article v-for="recall in openRecalls" :key="recall.code" class="rdw-recall">
        <div class="rdw-recall__header">
          <font-awesome-icon icon="triangle-exclamation" class="rdw-recall__icon" />
          <span class="rdw-recall__code">{{ recall.code }}</span>
          <StatusBadge v-if="severityLabel(recall)" :variant="severityVariant(recall)">
            {{ severityLabel(recall) }}
          </StatusBadge>
        </div>
        <p v-if="recall.defect" class="rdw-recall__text" lang="nl">{{ recall.defect }}</p>
        <dl class="rdw-recall__details">
          <template v-if="recall.risk">
            <dt>{{ t('rdw.recallRisk') }}</dt>
            <dd lang="nl">{{ recall.risk }}</dd>
          </template>
          <template v-if="recall.repair">
            <dt>{{ t('rdw.recallRepair') }}</dt>
            <dd lang="nl">{{ recall.repair }}</dd>
          </template>
        </dl>
      </article>

      <template v-if="repairedRecalls.length > 0">
        <div class="detail-modal__section-title mt-3">{{ t('rdw.repairedTitle') }}</div>
        <div class="detail-list">
          <DetailListItem
            v-for="recall in repairedRecalls"
            :key="recall.code"
            icon="check"
            :label="recall.defect ?? t('rdw.repaired')"
            :value="recall.code"
          />
        </div>
      </template>

      <p v-if="known.recalls.length > 0" class="rdw-card__meta">{{ t('rdw.dutchTexts') }}</p>
    </template>

    <p v-if="fetchedAt" class="rdw-card__meta">{{ fetchedAt }} · {{ t('rdw.source') }}</p>
    <p v-if="info?.lastError" class="rdw-card__meta text-danger" :title="info.lastError">
      {{ t('rdw.lastAttemptFailed') }}
    </p>
  </ExpandableStatusCard>
</template>
