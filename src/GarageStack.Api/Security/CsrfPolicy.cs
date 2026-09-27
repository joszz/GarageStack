namespace GarageStack.Api.Security;

internal static class CsrfPolicy
{
    internal static bool IsOriginAllowed(string origin, IEnumerable<string> allowedOrigins) =>
        allowedOrigins.Any(o => string.Equals(o, origin, StringComparison.OrdinalIgnoreCase));
}
