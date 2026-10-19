namespace GarageStack.Core.Models;

/// <summary>The registration fields GarageStack reads from Gekentekende voertuigen (m9d7-ebf2).</summary>
public sealed record RdwRegistration(
    string? Make,
    string? Model,
    DateOnly? ApkExpiresOn,
    bool? WamInsured,
    bool? OpenRecallIndicator);

/// <summary>One row of Terugroep_actie_status (t49b-isb7): a recall on the plate and where it stands.</summary>
public sealed record RdwRecallStatus(string Code, string Status);

/// <summary>
/// One row of Terugroep_actie (j9yg-7rg9): what a recall is about, what can happen because of it,
/// what the dealer does, and the RDW's rating of the risk (<see cref="RdwRecall.Severity"/>).
/// </summary>
public sealed record RdwRecallDetails(string Code, string? Defect, string? Risk, string? Repair, string? Severity);

/// <summary>The RDW could not be asked, or did not answer usably. The message is kept as the lookup's last error.</summary>
public sealed class RdwUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
