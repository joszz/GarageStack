using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;

namespace GarageStack.Data.Demo;

/// <summary>
/// The RDW as the demo car sees it, whatever plate is entered: an APK due in three weeks, one open
/// recall and one the dealer has already repaired. Nothing leaves the machine.
/// </summary>
public sealed class DemoRdwClient(TimeProvider time) : IRdwClient
{
    public const string OpenRecallCode = "MGP260001";
    public const string RepairedRecallCode = "MGP250002";

    public const int ApkDaysLeft = 21;

    public static RdwRegistration Registration(DateTime utcNow) => new(
        "MG",
        "MG ZS EV",
        ZoneTime.DateOf(utcNow, ApkReminder.DutchZone).AddDays(ApkDaysLeft),
        WamInsured: true,
        OpenRecallIndicator: true);

    public static readonly IReadOnlyList<RdwRecallStatus> Statuses =
    [
        new(OpenRecallCode, RdwRecall.OpenStatus),
        new(RepairedRecallCode, RdwRecall.RepairedStatus),
    ];

    // Worded the way the RDW words its recalls, which are in Dutch only.
    public static readonly IReadOnlyList<RdwRecallDetails> Details =
    [
        new(OpenRecallCode,
            "De software van de laadmodule kan bij snelladen een onjuiste temperatuur doorgeven.",
            "Het laden kan onverwacht stoppen en in uitzonderlijke gevallen kan de laadkabel oververhit raken.",
            "De voertuigeigenaar wordt verzocht een afspraak te maken met een merkdealer. Deze zal de software van de laadmodule bijwerken.",
            "GEM"),
        new(RepairedRecallCode,
            "De bevestiging van de bestuurdersstoel is mogelijk niet goed aangehaald.",
            "Bij een aanrijding kan de stoel loskomen, waardoor het letselrisico toeneemt.",
            "De dealer controleert de bevestigingsbouten en haalt ze aan.",
            "ERN"),
    ];

    public Task<RdwRegistration?> GetRegistrationAsync(string plate, CancellationToken ct = default) =>
        Task.FromResult<RdwRegistration?>(Registration(time.GetUtcNow().UtcDateTime));

    public Task<IReadOnlyList<RdwRecallStatus>> GetRecallStatusesAsync(string plate, CancellationToken ct = default) =>
        Task.FromResult(Statuses);

    public Task<IReadOnlyList<RdwRecallDetails>> GetRecallDetailsAsync(IReadOnlyCollection<string> codes, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<RdwRecallDetails>>(Details.Where(d => codes.Contains(d.Code)).ToList());
}
