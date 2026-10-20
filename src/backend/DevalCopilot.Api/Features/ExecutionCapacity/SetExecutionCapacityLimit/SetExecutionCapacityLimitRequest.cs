namespace DevalCopilot.Api.Features.ExecutionCapacity.SetExecutionCapacityLimit;

/// <summary>The only caller-supplied fact of an explicit recorded-mutating limit change (ADR-0059): exactly one JSON integer member,
/// <c>limit</c>, valued one or two. Missing, null, mistyped, coerced, unknown, duplicate, differently cased and escaped members and any
/// other value are refused before a command can change state.</summary>
public sealed record SetExecutionCapacityLimitRequest
{
    public required int Limit { get; init; }
}
