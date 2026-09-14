namespace GarageStack.Api.Authentication;

/// <summary>
/// Guards the return URL that travels through the OIDC round-trip. Only same-origin, path-only
/// URLs survive, so a crafted /api/auth/oidc/login?returnUrl=https://evil.example cannot turn
/// the sign-in flow into an open redirect.
/// </summary>
internal static class LocalRedirect
{
    internal const string Default = "/";

    private const int MaxLength = 512;

    internal static string Sanitize(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
            return Default;

        var value = returnUrl.Trim();

        if (value.Length > MaxLength)
            return Default;

        // "//host" and "/\host" are protocol-relative URLs pointing at another origin.
        if (!value.StartsWith('/') || value.StartsWith("//", StringComparison.Ordinal) || value.StartsWith("/\\", StringComparison.Ordinal))
            return Default;

        // Control characters (CR/LF in particular) could otherwise be smuggled into the
        // Location response header.
        if (value.Any(char.IsControl))
            return Default;

        return value;
    }
}
