import { request, requestJson, send } from '@/services/apiCore'

export type MaintenanceDueStatus = 'unknown' | 'ok' | 'dueSoon' | 'overdue'

export interface MaintenanceItem {
  id: number
  vehicleId: number
  name: string
  notes: string | null
  intervalKm: number | null
  intervalMonths: number | null
  lastServiceDate: string | null
  lastServiceOdometerKm: number | null
  dueStatus: MaintenanceDueStatus
  nextDueDate: string | null
  nextDueOdometerKm: number | null
  daysRemaining: number | null
  kmRemaining: number | null
  createdAt: string
}

export interface MaintenanceLogEntry {
  id: number
  maintenanceItemId: number
  performedAt: string
  odometerKm: number | null
  notes: string | null
  createdAt: string
}

export interface CreateMaintenanceItemRequest {
  name: string
  notes?: string | null
  intervalKm?: number | null
  intervalMonths?: number | null
  lastServiceDate?: string | null
  lastServiceOdometerKm?: number | null
}

export interface UpdateMaintenanceItemRequest {
  name: string
  notes?: string | null
  intervalKm?: number | null
  intervalMonths?: number | null
}

export interface LogMaintenanceServiceRequest {
  performedAt: string
  odometerKm?: number | null
  notes?: string | null
}

export interface LogMaintenanceServiceResponse {
  item: MaintenanceItem
  logEntry: MaintenanceLogEntry
}

export const maintenanceApi = {
  list: (vin: string) => request<MaintenanceItem[]>(`/api/vehicles/${vin}/maintenance`),
  create: (vin: string, body: CreateMaintenanceItemRequest) =>
    requestJson<MaintenanceItem>(`/api/vehicles/${vin}/maintenance`, 'POST', body),
  update: (vin: string, id: number, body: UpdateMaintenanceItemRequest) =>
    requestJson<MaintenanceItem>(`/api/vehicles/${vin}/maintenance/${id}`, 'PUT', body),
  delete: (vin: string, id: number) => send(`/api/vehicles/${vin}/maintenance/${id}`, 'DELETE'),
  listLog: (vin: string, id: number) =>
    request<MaintenanceLogEntry[]>(`/api/vehicles/${vin}/maintenance/${id}/log`),
  logService: (vin: string, id: number, body: LogMaintenanceServiceRequest) =>
    requestJson<LogMaintenanceServiceResponse>(
      `/api/vehicles/${vin}/maintenance/${id}/log`,
      'POST',
      body,
    ),
  deleteLogEntry: (vin: string, id: number, logId: number) =>
    send(`/api/vehicles/${vin}/maintenance/${id}/log/${logId}`, 'DELETE'),
}
