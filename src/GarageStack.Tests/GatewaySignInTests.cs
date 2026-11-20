using GarageStack.Api.Endpoints;
using GarageStack.Api.Services;
using GarageStack.Core.Models;
using GarageStack.Worker.Mqtt;
using Microsoft.Extensions.Logging.Abstractions;

namespace GarageStack.Tests;

// The app tells "the gateway cannot sign in to MG" apart from "the car is offline": the Worker
// works it out from the gateway's account topics, its heartbeat carries it, the API tells browsers.
public class GatewaySignInTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 20, 6, 18, 0, TimeSpan.Zero);

    [Fact]
    public void State_BeforeTheGatewaySaidAnything_IsUnknown()
    {
        Assert.Null(new GatewaySignInState().SignedIn);
    }

    [Fact]
    public void State_OnlyAGoodSignIn_IsSignedIn()
    {
        var state = new GatewaySignInState();
        state.SignedInAt(T0);

        Assert.True(state.SignedIn);
    }

    [Fact]
    public void State_AFailureAfterTheLastGoodSignIn_IsSignedOut()
    {
        var state = new GatewaySignInState();
        state.SignedInAt(T0);
        state.FailedAt(T0.AddMinutes(1));

        Assert.False(state.SignedIn);
    }

    // Both topics are retained, so after a reconnect they come back in either order.
    [Fact]
    public void State_TheNewerOfTheTwoDecides_WhateverOrderTheyArriveIn()
    {
        var state = new GatewaySignInState();
        state.SignedInAt(T0.AddMinutes(15));
        state.FailedAt(T0);

        Assert.True(state.SignedIn);

        state.SignedInAt(T0.AddMinutes(-5));
        Assert.True(state.SignedIn);
    }

    [Fact]
    public void Heartbeat_FromAnOlderWorker_HasNoSignInToSay()
    {
        var heartbeat = WorkerHeartbeatPayload.FromJson("""{"mqttConnected":true}""");

        Assert.NotNull(heartbeat);
        Assert.Null(heartbeat.GatewaySignedIn);
    }

    [Fact]
    public void Heartbeat_CarriesTheSignIn()
    {
        var json = new WorkerHeartbeatPayload(MqttConnected: true, GatewaySignedIn: false).ToJson();

        Assert.Equal(false, WorkerHeartbeatPayload.FromJson(json)?.GatewaySignedIn);
    }

    [Fact]
    public async Task Monitor_TellsEveryBrowserOnce_PerChange()
    {
        var ct = TestContext.Current.CancellationToken;
        var hub = new RecordingHub();
        var monitor = new GatewaySignInMonitor(hub, NullLogger<GatewaySignInMonitor>.Instance);

        await monitor.UpdateAsync(true, ct);
        await monitor.UpdateAsync(true, ct);
        await monitor.UpdateAsync(false, ct);

        Assert.Equal(false, monitor.SignedIn);
        Assert.Equal(
            [(RecordingHub.AllGroup, GatewaySignInMonitor.ChangedEvent, (object?)new GatewayStatus(true)),
             (RecordingHub.AllGroup, GatewaySignInMonitor.ChangedEvent, new GatewayStatus(false))],
            hub.Sent);
    }
}
