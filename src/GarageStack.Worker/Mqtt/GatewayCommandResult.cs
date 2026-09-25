namespace GarageStack.Worker.Mqtt;

/// <summary>
/// The gateway's answer to a command. After handling {topic}/set it publishes "Success" or
/// "Failed: {reason}" on {topic}/result (saic-python-mqtt-gateway, handlers/vehicle_command.py).
/// The answer carries no request id, so it says which command it answers but not which request.
/// <c>Topic</c> is the command's topic without the /result suffix (e.g. doors/locked); <c>Detail</c>
/// is the gateway's reason for a failure, or null on success or when it gave none.
/// </summary>
public readonly record struct GatewayCommandResult(string Topic, bool Success, string? Detail)
{
    private const string ResultSuffix = "/result";
    private const string SuccessPayload = "Success";
    private const string FailedPrefix = "Failed";

    // The reason travels through pg_notify, which refuses payloads over 8000 bytes, and ends up on
    // a phone screen. A real reason is one sentence; anything longer is exception text.
    internal const int MaxDetailLength = 300;

    /// <returns>
    /// false when <paramref name="subtopic"/> is not a result topic, or its payload is neither
    /// answer the gateway gives, so the caller treats it like any other unrecognised topic.
    /// </returns>
    public static bool TryParse(string subtopic, string payload, out GatewayCommandResult result)
    {
        result = default;
        if (subtopic.Length <= ResultSuffix.Length || !subtopic.EndsWith(ResultSuffix, StringComparison.Ordinal))
            return false;

        var topic = subtopic[..^ResultSuffix.Length];

        if (payload == SuccessPayload)
        {
            result = new GatewayCommandResult(topic, true, null);
            return true;
        }

        if (!payload.StartsWith(FailedPrefix, StringComparison.Ordinal))
            return false;

        var detail = payload[FailedPrefix.Length..].TrimStart(':').Trim().ReplaceLineEndings(" ");
        if (detail.Length > MaxDetailLength)
            detail = detail[..MaxDetailLength];

        result = new GatewayCommandResult(topic, false, detail.Length > 0 ? detail : null);
        return true;
    }
}
