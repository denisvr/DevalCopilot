using System.Text.Json.Serialization;

namespace DevalCopilot.Api.Features.Runs.SetCodexAccountUsageWarning;

/// <summary><c>Percent</c> must be present: a whole number from 1 through 100 sets the warning and an explicit <c>null</c> clears it.
/// An omitted member is rejected rather than read as a clear.</summary>
[JsonConverter(typeof(SetCodexAccountUsageWarningRequestConverter))]
public sealed record SetCodexAccountUsageWarningRequest
{
    public required int? Percent { get; init; }
}
