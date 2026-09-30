using System.Text.Json.Serialization;

namespace DevalCopilot.Api.Features.Runs.SetClaudeMutationTurnLimit;

/// <summary><c>MaxTurns</c> must be present: a whole number from 1 through 100 sets the request and an explicit
/// <c>null</c> clears it. An omitted member is rejected rather than read as a clear.</summary>
[JsonConverter(typeof(SetClaudeMutationTurnLimitRequestConverter))]
public sealed record SetClaudeMutationTurnLimitRequest
{
    public required int? MaxTurns { get; init; }
}
