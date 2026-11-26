import { beforeEach, describe, expect, it, vi } from 'vitest'
import { reactive, ref } from 'vue'
import { useRefresh } from '../useRefresh'

const store = vi.hoisted(() => ({
  state: null as { activeVin: string | null; loading: boolean } | null,
}))
const command = vi.hoisted(() => ({
  send: vi.fn<(vin: string, command: string, mode: string) => void>(),
  sending: null as { value: string | null } | null,
}))
const connection = vi.hoisted(() => ({ reachable: true }))

vi.mock('@/stores/vehicle', () => ({ useVehicleStore: () => store.state }))
vi.mock('@/composables/useVehicleCommand', () => ({
  useVehicleCommand: () => ({ send: command.send, sending: command.sending }),
}))
vi.mock('@/composables/useConnection', () => ({ useConnection: () => connection }))

beforeEach(() => {
  store.state = reactive({ activeVin: 'FAKEVN00000000001', loading: false })
  command.sending = ref<string | null>(null)
  command.send.mockClear()
  connection.reachable = true
})

describe('useRefresh', () => {
  it('asks the chosen car for fresh data', () => {
    const { refresh, disabled } = useRefresh()

    expect(disabled.value).toBe(false)
    refresh()

    expect(command.send).toHaveBeenCalledWith('FAKEVN00000000001', 'refresh', 'force')
  })

  it('waits while a refresh runs, whether the store loads or the command is out', () => {
    const { refreshing, disabled } = useRefresh()

    store.state!.loading = true
    expect(refreshing.value).toBe(true)
    expect(disabled.value).toBe(true)

    store.state!.loading = false
    command.sending!.value = 'refresh'
    expect(refreshing.value).toBe(true)
  })

  it('sends nothing before a car is chosen', () => {
    store.state!.activeVin = null
    const { refresh, disabled } = useRefresh()

    expect(disabled.value).toBe(true)
    refresh()
    expect(command.send).not.toHaveBeenCalled()
  })

  it('is not offered without the server', () => {
    connection.reachable = false
    expect(useRefresh().canRefresh.value).toBe(false)
  })
})
