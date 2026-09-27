using System.Security.Cryptography;
using System.Text;

namespace GarageStack.Api.Security;

/// <summary>Compares a submitted secret (a password, an API key) with the configured one.</summary>
internal static class SecretComparer
{
    /// <summary>
    /// Equal in constant time, so how long the comparison takes says nothing about how much of
    /// the secret was right.
    /// </summary>
    internal static bool FixedTimeEquals(string left, string right)
    {
        // Hash both values first so the compared buffers always have identical length.
        var leftBytes = SHA256.HashData(Encoding.UTF8.GetBytes(left));
        var rightBytes = SHA256.HashData(Encoding.UTF8.GetBytes(right));

        return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
