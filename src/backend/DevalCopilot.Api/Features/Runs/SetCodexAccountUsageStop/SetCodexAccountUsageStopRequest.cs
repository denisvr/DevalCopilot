using System.Text.Json.Serialization;

namespace DevalCopilot.Api.Features.Runs.SetCodexAccountUsageStop;

/// <summary><c>Percent</c> must be present: a whole number from 1 through 100 sets the stop and an explicit <c>null</c> clears it. An
/// omitted member is rejected rather than read as a clear.</summary>
[JsonConverter(typeof(SetCodexAccountUsageStopRequestConverter))]
public sealed record SetCodexAccountUsageStopRequest
{
    public required int? Percent { get; init; }
}
