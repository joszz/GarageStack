using Microsoft.Extensions.Configuration;

namespace GarageStack.Core.Configuration;

/// <summary>The Open Charge Map key (OPENCHARGEMAP_API_KEY). Without one the charging station layer stays off.</summary>
public sealed class OpenChargeMapOptions
{
    public string? ApiKey { get; init; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    public static OpenChargeMapOptions From(IConfiguration configuration) => new()
    {
        ApiKey = configuration["OpenChargeMap:ApiKey"],
    };
}
