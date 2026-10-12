using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace GarageStack.Core.Helpers;

/// <summary>
/// A GarageStack version as MinVer stamps it (0.9.2, 0.9.3-preview.0.4+abc123) or as a release
/// tag names it (v0.9.2). Only what the update check needs: the number, and whether it is a build
/// on the way to that number rather than the release itself.
/// </summary>
public sealed partial record ReleaseVersion(int Major, int Minor, int Patch, bool IsPreRelease)
{
    [GeneratedRegex(@"^v?(\d+)\.(\d+)\.(\d+)(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]*)?$")]
    private static partial Regex Pattern();

    public static bool TryParse(string? text, out ReleaseVersion version)
    {
        var match = Pattern().Match(text?.Trim() ?? string.Empty);
        if (!match.Success
            || !TryPart(match.Groups[1].Value, out var major)
            || !TryPart(match.Groups[2].Value, out var minor)
            || !TryPart(match.Groups[3].Value, out var patch))
        {
            version = null!;
            return false;
        }

        version = new ReleaseVersion(major, minor, patch, match.Groups[4].Success);
        return true;

        static bool TryPart(string text, out int value) =>
            int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// Whether this build carries a version at all. A build from source without APP_VERSION is
    /// stamped 0.0.0-local, and every release is newer than that, so it says nothing.
    /// </summary>
    public bool IsKnown => (Major, Minor, Patch) != (0, 0, 0);

    /// <summary>
    /// Whether <paramref name="release"/> is newer than this build. MinVer stamps a build made
    /// after a release as the next patch with a pre-release suffix (0.9.3-preview.0.4 after
    /// 0.9.2), so that build is already past 0.9.2, and 0.9.3 itself is newer than it.
    /// </summary>
    public bool IsOlderThan(ReleaseVersion release)
    {
        var order = (Major, Minor, Patch).CompareTo((release.Major, release.Minor, release.Patch));
        return order < 0 || (order == 0 && IsPreRelease && !release.IsPreRelease);
    }

    /// <summary>The release tag for this version, as GitHub's release pages name it.</summary>
    public string Tag => $"v{Major}.{Minor}.{Patch}";

    public override string ToString() => $"{Major}.{Minor}.{Patch}";

    /// <summary>
    /// The version <paramref name="assembly"/> was built as: its informational version, which
    /// MinVer sets from the git tag, or from APP_VERSION in a Docker build.
    /// </summary>
    public static ReleaseVersion? Of(Assembly assembly) =>
        TryParse(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion, out var version)
            ? version
            : null;
}
