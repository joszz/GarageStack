using GarageStack.Api.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace GarageStack.Tests;

/// <summary>
/// What the API sends to browsers over SignalR, recorded by group and method. A message to every
/// browser is recorded under <see cref="AllGroup"/>.
/// </summary>
internal sealed class RecordingHub : IHubContext<TelemetryHub>, IHubClients
{
    public const string AllGroup = "*";

    private readonly TaskCompletionSource _resultSent = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public List<(string Group, string Method, object? Arg)> Sent { get; } = [];

    IHubClients IHubContext<TelemetryHub>.Clients => this;
    IGroupManager IHubContext<TelemetryHub>.Groups => throw new NotSupportedException();

    /// <summary>The command result sent, once one has been.</summary>
    public async Task<object?> WaitForAsync(string method)
    {
        await _resultSent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        lock (Sent) return Sent.Single(s => s.Method == method).Arg;
    }

    public IClientProxy Group(string groupName) => new Proxy(this, groupName);
    public IClientProxy All => new Proxy(this, AllGroup);
    public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
    public IClientProxy Client(string connectionId) => throw new NotSupportedException();
    public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();
    public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
    public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();
    public IClientProxy User(string userId) => throw new NotSupportedException();
    public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();

    private sealed class Proxy(RecordingHub hub, string group) : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            lock (hub.Sent) hub.Sent.Add((group, method, args.FirstOrDefault()));
            if (method == "commandResult") hub._resultSent.TrySetResult();
            return Task.CompletedTask;
        }
    }
}
