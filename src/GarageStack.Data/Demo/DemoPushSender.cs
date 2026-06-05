using GarageStack.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace GarageStack.Data.Demo;

public sealed class DemoPushSender : IPushSender
{
    private readonly ILogger<DemoPushSender> _logger;

    public DemoPushSender(ILogger<DemoPushSender> logger) => _logger = logger;

    public Task SendToAllAsync(string title, string body, CancellationToken ct = default, string? category = null)
    {
        _logger.LogDebug("Demo mode: suppressed push notification '{Title}' (category={Category})", title, category);
        return Task.CompletedTask;
    }
}
