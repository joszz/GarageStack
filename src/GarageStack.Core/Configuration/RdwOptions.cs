using Microsoft.Extensions.Configuration;

namespace GarageStack.Core.Configuration;

/// <summary>
/// Whether the car's Dutch registration is looked up at the RDW, and how often (RDW_ENABLED,
/// RDW_REFRESH_HOURS, RDW_APP_TOKEN). Off unless the operator turns it on: the plate is the one
/// thing that leaves the network, but it is still something leaving it. The RDW's open data needs
/// no key; a Socrata app token only lifts the rate limit anonymous callers share.
/// </summary>
public sealed class RdwOptions
{
    public const string BaseUrl = "https://opendata.rdw.nl";

    public const int DefaultRefreshHours = 24;

    public bool Enabled { get; init; }

    /// <summary>How old a lookup may get before the Worker asks again. One hour at the least.</summary>
    public int RefreshHours { get; init; } = DefaultRefreshHours;

    /// <summary>Sent as X-App-Token when set.</summary>
    public string? AppToken { get; init; }

    public TimeSpan RefreshInterval => TimeSpan.FromHours(RefreshHours);

    /// <summary>Only an explicit "true" turns lookups on; a blank or unparseable interval means a day.</summary>
    public static RdwOptions From(IConfiguration configuration) => new()
    {
        Enabled = configuration.SwitchOrDefault("Rdw:Enabled", fallback: false),
        RefreshHours = Math.Max(1, configuration.IntegerOrDefault("Rdw:RefreshHours", DefaultRefreshHours)),
        AppToken = configuration["Rdw:AppToken"] is { } token && !string.IsNullOrWhiteSpace(token) ? token.Trim() : null,
    };
}
