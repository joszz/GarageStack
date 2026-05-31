namespace GarageStack.Core.Interfaces;

public interface IPushSender
{
    Task SendToAllAsync(string title, string body, CancellationToken ct = default, string? category = null);
}
