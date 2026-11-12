using System.Globalization;
using System.Text;

namespace GarageStack.Data.Demo;

/// <summary>
/// The Worker's log file for demo mode, which runs no Worker: a day of the entries a real one
/// writes, in the file format LogFileReader reads, so the log viewer shows both services instead
/// of asking for WORKER_LOG_DIRECTORY. Written once per process to a folder of its own under the
/// temp directory, dated back from when it is first asked for.
/// </summary>
public static class DemoWorkerLog
{
    private const string MaskedVin = "***2345";

    private static readonly Lazy<string> Folder = new(() => Write(DateTimeOffset.Now));

    /// <summary>The folder holding the demo Worker's log file, written on first use.</summary>
    public static string Directory => Folder.Value;

    /// <summary>
    /// The entries, oldest first, ending just before <paramref name="now"/>, each as Serilog writes
    /// it: "2026-10-16 06:30:38.268 +02:00 [INF] message".
    /// </summary>
    public static IReadOnlyList<string> Lines(DateTimeOffset now) =>
        [.. Entries().Select(e => Line(now - e.Ago, e.Level, e.Message))];

    private static string Write(DateTimeOffset now)
    {
        var folder = System.IO.Directory.CreateTempSubdirectory("garagestack-demo-worker-logs-").FullName;
        var file = Path.Combine(folder, $"worker-{now:yyyyMMdd}.log");
        File.WriteAllLines(file, Lines(now), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return folder;
    }

    private static string Line(DateTimeOffset at, string level, string message) =>
        $"{at.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture)} [{level}] {message}";

    // How long before now each entry was written, newest last: a start, a night of quiet jobs, a
    // broker that went away for a moment, a morning drive and what follows it.
    private static IEnumerable<(TimeSpan Ago, string Level, string Message)> Entries()
    {
        static TimeSpan M(double minutes) => TimeSpan.FromMinutes(minutes);
        return
        [
            (M(1310), "INF", "Connected to MQTT broker at mosquitto:1883"),
            (M(1310) - TimeSpan.FromSeconds(1), "INF", "Subscribed to saic/# and homeassistant/#"),
            (M(1309.9), "INF", "Trip recorder started"),
            (M(1309.9), "INF", "Climate schedules started"),
            (M(1309.8), "INF", "Dawarich phone fusion started"),
            (M(1309), "INF", $"HA discovery - VIN={MaskedVin} hw_version=EP21 model=ZS EV"),
            (M(1308), "INF", "Pre-cached 12 of 12 Overpass fuel tiles for " + MaskedVin),
            (M(1307.5), "INF", "Pre-cached 9 of 9 OCM charging tiles for " + MaskedVin),
            (M(1080), "INF", "Housekeeping removed 214 expired cache entries and 3 deleted notifications"),
            (M(960), "WRN", "MQTT broker disconnected, will reconnect..."),
            (M(959.9), "INF", "Connected to MQTT broker at mosquitto:1883"),
            (M(959.9) - TimeSpan.FromSeconds(1), "INF", "Subscribed to saic/# and homeassistant/#"),
            (M(720), "INF", $"Compacted old telemetry for VIN={MaskedVin}: 96 row(s) folded into quarter-hour rows"),
            (M(310), "INF", "Climate schedule 1 ran: Started"),
            (M(296), "INF", $"Engine started for VIN={MaskedVin} - sending push notification"),
            (M(282), "INF", "Trip completed for vehicleId=1 - notifying SignalR clients"),
            (M(280), "INF", $"Saved 1 finished trip(s) for VIN={MaskedVin}"),
            (M(264), "INF", "Phone fusion trip 9: Fused, 41 points added"),
            (M(240), "INF", $"Maintenance push sent: {MaskedVin}/maintenance-due - Tyre rotation due soon"),
            (M(180), "WRN", $"Command climate/remoteClimateState failed for VIN={MaskedVin}: vehicle is busy, return code 8"),
            (M(179), "INF", $"Command climate/remoteClimateState succeeded for VIN={MaskedVin}"),
            (M(95), "INF", $"RDW push sent: {MaskedVin}/rdw-apk"),
            (M(42), "INF", "Vehicle 1 arrived at commute place 2"),
            (M(40), "INF", $"Saved 1 finished trip(s) for VIN={MaskedVin}"),
            (M(24), "INF", "Phone fusion trip 10: NoMatch, 0 points added"),
            (M(6), "INF", "Housekeeping removed 37 expired cache entries and 0 deleted notifications"),
        ];
    }
}
