namespace GarageStack.Worker.Mqtt;

public static class MqttTopicParser
{
    // VIN is exactly 17 alphanumeric characters (I, O, Q excluded per ISO 3779).
    private static readonly System.Text.RegularExpressions.Regex VinPattern =
        new(@"^[A-HJ-NPR-Z0-9]{17}$", System.Text.RegularExpressions.RegexOptions.None);

    public static bool TryExtractVin(string topic, out string vin)
    {
        // Topic pattern: saic/<user>/vehicles/<VIN>/...
        var parts = topic.Split('/');
        if (parts.Length >= 4 && parts[0] == "saic" && parts[2] == "vehicles")
        {
            var candidate = parts[3];
            if (VinPattern.IsMatch(candidate))
            {
                vin = candidate;
                return true;
            }
        }
        vin = string.Empty;
        return false;
    }

    public static string ExtractSubtopic(string topic)
    {
        var parts = topic.Split('/');
        return parts.Length > 4 ? string.Join('/', parts[4..]) : string.Empty;
    }

    public static bool TryExtractUser(string topic, out string user)
    {
        var parts = topic.Split('/');
        if (parts.Length >= 2 && parts[0] == "saic" && !string.IsNullOrWhiteSpace(parts[1]))
        {
            user = parts[1];
            return true;
        }
        user = string.Empty;
        return false;
    }
}
