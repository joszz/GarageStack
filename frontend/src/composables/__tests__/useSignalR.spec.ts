import { afterEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h } from 'vue'
import { flushPromises, mount } from '@vue/test-utils'
import { useSignalR, type SignalRCallbacks } from '@/composables/useSignalR'

// Every connection the fake builder hands out, in order.
const hub = vi.hoisted(() => ({
  connections: [] as Array<Record<string, ReturnType<typeof vi.fn>>>,
}))

vi.mock('@microsoft/signalr', () => {
  class HubConnectionBuilder {
    withUrl() {
      return this
    }
    withAutomaticReconnect() {
      return this
    }
    configureLogging() {
      return this
    }
    build() {
      const connection = {
        start: vi.fn<() => Promise<void>>(() => Promise.resolve()),
        stop: vi.fn<() => Promise<void>>(() => Promise.resolve()),
        invoke: vi.fn<(method: string, ...args: unknown[]) => Promise<void>>(() =>
          Promise.resolve(),
        ),
        on: vi.fn<(event: string, handler: unknown) => void>(),
        onreconnecting: vi.fn<(handler: unknown) => void>(),
        onreconnected: vi.fn<(handler: unknown) => void>(),
        onclose: vi.fn<(handler: unknown) => void>(),
      }
      hub.connections.push(connection)
      return connection
    }
  }
  return { HubConnectionBuilder, LogLevel: { Warning: 3 } }
})

const callbacks: SignalRCallbacks = {
  onTelemetryUpdated: vi.fn<SignalRCallbacks['onTelemetryUpdated']>(),
  onNotificationReceived: vi.fn<SignalRCallbacks['onNotificationReceived']>(),
  onTripCompleted: vi.fn<SignalRCallbacks['onTripCompleted']>(),
  onCommandResult: vi.fn<SignalRCallbacks['onCommandResult']>(),
}

function mountSignalR() {
  const state: { api: ReturnType<typeof useSignalR> | null } = { api: null }
  const wrapper = mount(
    defineComponent({
      setup() {
        state.api = useSignalR(callbacks)
        return () => h('div')
      },
    }),
  )
  return { wrapper, api: state.api! }
}

afterEach(() => {
  hub.connections.length = 0
})

describe('useSignalR', () => {
  it('connects and joins the vehicle group', async () => {
    const { api } = mountSignalR()

    await api.start(7)

    expect(hub.connections).toHaveLength(1)
    expect(hub.connections[0]!.start).toHaveBeenCalledOnce()
    expect(hub.connections[0]!.invoke).toHaveBeenCalledWith('JoinVehicle', 7)
    expect(api.connected.value).toBe(true)
  })

  it('opens nothing when stopped while the client library is still loading', async () => {
    const { api } = mountSignalR()

    const starting = api.start(7)
    await api.stop()
    await starting
    await flushPromises()

    expect(hub.connections).toHaveLength(0)
    expect(api.connected.value).toBe(false)
  })

  it('lets a newer start win over one still waiting for the library', async () => {
    const { api } = mountSignalR()

    const first = api.start(7)
    const second = api.start(8)
    await Promise.all([first, second])

    expect(hub.connections).toHaveLength(1)
    expect(hub.connections[0]!.invoke).toHaveBeenCalledWith('JoinVehicle', 8)
    expect(hub.connections[0]!.invoke).not.toHaveBeenCalledWith('JoinVehicle', 7)
  })

  it('closes the connection when the owning component unmounts', async () => {
    const { wrapper, api } = mountSignalR()
    await api.start(7)

    wrapper.unmount()
    await flushPromises()

    expect(hub.connections[0]!.stop).toHaveBeenCalledOnce()
  })
})
