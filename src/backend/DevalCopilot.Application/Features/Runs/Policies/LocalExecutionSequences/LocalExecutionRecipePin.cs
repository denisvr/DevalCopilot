namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;

/// <summary>One pinned recipe of a candidate, by value, in its position of the whole enabled set.</summary>
internal sealed record LocalExecutionRecipePin(
    int Ordinal,
    Guid CommandId,
    int CommandNumber,
    string Name,
    string ExecutablePath,
    string ArgumentsJson,
    int TimeoutSeconds,
    bool IsEnabled,
    DateTimeOffset ConfiguredAtUtc,
    DateTimeOffset UpdatedAtUtc);
