using Microsoft.Extensions.Configuration;

namespace GarageStack.Core.Configuration;

public sealed class DawarichOptions
{
    public string? Url { get; init; }
    public string? ApiKey { get; init; }
    public int FusionDelayMinutes { get; init; } = 15;
    public bool Enabled => Url is not null && ApiKey is not null;

    public static DawarichOptions From(IConfiguration configuration)
    {
        var url = configuration["Dawarich:Url"]?.Trim().TrimEnd('/');
        var key = configuration["Dawarich:ApiKey"]?.Trim();
        if (!string.IsNullOrEmpty(url) &&
            (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
             uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0 ||
             uri.Query.Length > 0 || uri.Fragment.Length > 0))
            throw new InvalidOperationException("DAWARICH_URL must be an HTTP(S) base URL without credentials, query or fragment.");

        var delay = configuration.IntegerOrDefault("Dawarich:FusionDelayMinutes", 15);
        if (delay is < 1 or > 1440)
            throw new InvalidOperationException("DAWARICH_FUSION_DELAY_MINUTES must be between 1 and 1440.");
        return new()
        {
            Url = string.IsNullOrEmpty(url) ? null : url,
            ApiKey = string.IsNullOrEmpty(key) ? null : key,
            FusionDelayMinutes = delay,
        };
    }
}
