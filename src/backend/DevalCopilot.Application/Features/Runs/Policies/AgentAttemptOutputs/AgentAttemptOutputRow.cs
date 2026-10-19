namespace DevalCopilot.Application.Features.Runs.Policies.AgentAttemptOutputs;

/// <summary>The stored header of one message associated with the attempt, classified by the database without materializing a stored
/// enum: type, provenance and actor kind use 0 for a value not known; role and provider use 0 for a null column and 99 for a stored value
/// not known. No text, reply or attempt column is part of it.</summary>
internal sealed record AgentAttemptOutputRow(
    long Sequence,
    Guid Id,
    Guid RunId,
    DateTimeOffset OccurredAtUtc,
    bool VersionKnown,
    int TypeCode,
    int ProvenanceCode,
    int ActorKindCode,
    int ActorRoleCode,
    int ActorProviderCode);
