namespace GarageStack.Worker.Mqtt;

/// <summary>
/// Whether the gateway is signed in to the MG account right now, from the times of its last good
/// sign-in (account/lastLogin) and its last failed one (account/lastLoginError). Both are retained,
/// so they arrive again in any order after every reconnect: the newer of the two decides. Set by
/// <see cref="AccountStatusHandler"/>, read by the Worker's heartbeat, which tells the API.
/// </summary>
public sealed class GatewaySignInState
{
    private readonly Lock _lock = new();
    private DateTimeOffset? _lastLogin;
    private DateTimeOffset? _lastLoginError;

    /// <summary>True or false once the gateway has said either; null before it has said anything.</summary>
    public bool? SignedIn
    {
        get
        {
            lock (_lock)
            {
                if (_lastLoginError is not { } failedAt) return _lastLogin is null ? null : true;
                return _lastLogin is { } signedInAt && signedInAt >= failedAt;
            }
        }
    }

    internal void SignedInAt(DateTimeOffset at)
    {
        lock (_lock)
            if (_lastLogin is null || at > _lastLogin) _lastLogin = at;
    }

    internal void FailedAt(DateTimeOffset at)
    {
        lock (_lock)
            if (_lastLoginError is null || at > _lastLoginError) _lastLoginError = at;
    }
}
