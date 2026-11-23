using System.Text.RegularExpressions;

namespace GarageStack.Core.Helpers;

/// <summary>
/// Takes what points at a person or a car out of text that goes into the diagnostics download,
/// which is made to be attached to a public bug report. Each known car becomes car-1, car-2, ...
/// in the order given, both its full VIN and the shortened form the logs write
/// (<see cref="LogRedaction.Vin"/>), so a reader can still tell which lines are about which car.
/// Licence plates, email addresses, IP addresses, the host and query of URLs, a host name before a
/// port, the account name in a home folder, the name and groups of a single sign-in and anything
/// that looks like a coordinate are replaced too; loopback addresses and single-word host names
/// (the compose services: postgres, mosquitto) stay, as they identify nobody. It errs on the side
/// of removing: a long decimal that was not a position is lost, a position that slipped through
/// cannot be taken back.
/// </summary>
public sealed partial class DiagnosticsRedaction
{
    private const string UnknownCar = "car-?";

    private readonly List<(string Vin, string Label)> _cars = [];
    private readonly List<Regex> _plates = [];

    /// <param name="cars">The cars, in the order their labels are numbered, each with its plate if known.</param>
    public DiagnosticsRedaction(IEnumerable<(string Vin, string? LicensePlate)> cars)
    {
        foreach (var (vin, plate) in cars)
        {
            _cars.Add((vin, CarLabel(_cars.Count)));
            if (LicensePlate.Normalize(plate) is { Length: > 0 } normalized)
            {
                // The plate as stored, or written with dashes or spaces between any of its characters.
                var pattern = string.Join(@"[\s-]?", normalized.Select(c => Regex.Escape(c.ToString())));
                _plates.Add(new Regex(
                    $"(?<![A-Za-z0-9]){pattern}(?![A-Za-z0-9])",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)));
            }
        }
    }

    /// <summary>The label of the car at <paramref name="index"/> (from 0) in the list given.</summary>
    public static string CarLabel(int index) => $"car-{index + 1}";

    /// <summary><paramref name="text"/> with every identifier replaced; null stays null.</summary>
    public string? Scrub(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // URLs first. The host is often a personal domain and the query can carry a key, so both
        // go; the path stays (a stack trace's file names), and the rules below scrub it like the
        // rest. Loopback stays as it is: it says how the server is set up, not where.
        var scrubbed = Url().Replace(text, m =>
            $"{m.Groups["scheme"].Value}://{(IsLoopback(m.Groups["host"].Value) ? m.Groups["host"].Value : "[host]")}"
            + m.Groups["path"].Value
            + (m.Groups["query"].Success ? "?[query]" : ""));
        scrubbed = SaicAccount().Replace(scrubbed, "saic/[account]/vehicles/");
        scrubbed = HomeFolder().Replace(scrubbed, "${root}[user]");
        scrubbed = Email().Replace(scrubbed, "[email]");
        scrubbed = HostBeforePort().Replace(scrubbed, "[host]");
        scrubbed = SignInName().Replace(scrubbed, "[user]");
        scrubbed = SignInGroups().Replace(scrubbed, "groups [groups]");

        foreach (var (vin, label) in _cars)
            scrubbed = scrubbed.Replace(vin, label, StringComparison.OrdinalIgnoreCase);
        scrubbed = VinFormat.InText().Replace(scrubbed, UnknownCar);
        scrubbed = ShortenedVin().Replace(scrubbed, m => LabelForSuffix(m.Groups["suffix"].Value));

        foreach (var plate in _plates)
            scrubbed = plate.Replace(scrubbed, "[plate]");

        scrubbed = IPv4().Replace(scrubbed, m => m.Value == "127.0.0.1" ? m.Value : "[ip]");
        scrubbed = IPv6Candidate().Replace(scrubbed, m => m.Value != "::1" && IsIPv6(m.Value) ? "[ip]" : m.Value);
        return Coordinate().Replace(scrubbed, "[coord]");
    }

    // The shortened form only keeps the last four characters: two cars can share them, and then
    // there is no telling which one a line meant.
    private string LabelForSuffix(string suffix)
    {
        var matches = _cars.Where(c => c.Vin.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)).ToList();
        return matches.Count == 1 ? matches[0].Label : UnknownCar;
    }

    // Two colons in a row, or the full eight groups: a time of day (10:30:38) has neither.
    private static bool IsIPv6(string candidate) =>
        candidate.Contains("::", StringComparison.Ordinal) || candidate.Count(c => c == ':') == 7;

    private static bool IsLoopback(string host)
    {
        var name = host.StartsWith('[') ? host[..(host.IndexOf(']') + 1)] : host.Split(':')[0];
        return name is "localhost" or "127.0.0.1" or "[::1]";
    }

    // The host takes any user:password@ in front of it along; the query or fragment goes whole.
    [GeneratedRegex(@"(?<scheme>[A-Za-z][A-Za-z0-9+.-]*)://(?<host>[^\s/""'<>()?#]+)(?<path>/[^\s""'<>()?#]*)?(?<query>[?#][^\s""'<>()]*)?")]
    private static partial Regex Url();

    // The account name in a home folder of a stack trace's file paths (C:\Users\name\, /home/name/).
    [GeneratedRegex(@"(?<root>(?:[A-Za-z]:\\Users\\|/home/|/Users/))[^\\/\s]+", RegexOptions.IgnoreCase)]
    private static partial Regex HomeFolder();

    // A dotted host name before a port, as .NET writes a connection it could not make ("No such
    // host is known. (dawarich.example.org:443)"), often a personal domain. Not a script's name
    // before its line and column in a browser's stack (main.js:1:200), nor one in a path.
    [GeneratedRegex(@"(?<![\w.@/\\-])(?:[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?\.)+[A-Za-z]{2,}(?=:\d{1,5}(?![\d:]))")]
    private static partial Regex HostBeforePort();

    // The name the sign-in provider gave, in the API's single sign-in lines ("OIDC sign-in denied
    // for Jane Doe: ...", "OIDC sign-in for Jane Doe (...)"): kept in the log, so the owner can
    // tell who it was, but a name all the same.
    [GeneratedRegex(@"(?<=\bOIDC sign-in (?:denied )?for ).+?(?=: | \()")]
    private static partial Regex SignInName();

    // The groups a denied sign-in had ("groups [family, admins] do not match"), which can name people.
    [GeneratedRegex(@"\bgroups \[[^\]]*\]")]
    private static partial Regex SignInGroups();

    [GeneratedRegex(@"saic/[^/\s]+/vehicles/")]
    private static partial Regex SaicAccount();

    // Includes the shortened form LogRedaction.Account writes (j***@example.com).
    [GeneratedRegex(@"[A-Za-z0-9._%+*-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")]
    private static partial Regex Email();

    [GeneratedRegex(@"\*\*\*(?<suffix>[A-HJ-NPR-Z0-9]{4})(?![A-Za-z0-9])")]
    private static partial Regex ShortenedVin();

    // Not after "Version=": an assembly version in a stack trace (10.0.0.0) is not an address.
    [GeneratedRegex(@"(?<![\w.])(?<!Version=)(?:\d{1,3}\.){3}\d{1,3}(?![\w.])")]
    private static partial Regex IPv4();

    [GeneratedRegex(@"(?<![\w:])(?:[0-9A-Fa-f]{0,4}:){2,7}[0-9A-Fa-f]{0,4}(?![\w:])")]
    private static partial Regex IPv6Candidate();

    // A number in the range of a latitude or longitude with four or more decimals: about ten
    // metres or finer. Not a duration in milliseconds: every request line ends with one.
    [GeneratedRegex(@"(?<![\w.])-?\d{1,3}\.\d{4,}(?![\w.])(?!\s?ms\b)")]
    private static partial Regex Coordinate();
}
