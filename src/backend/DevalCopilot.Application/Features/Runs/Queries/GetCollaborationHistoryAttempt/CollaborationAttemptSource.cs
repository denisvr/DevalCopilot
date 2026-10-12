namespace DevalCopilot.Application.Features.Runs.Queries.GetCollaborationHistoryAttempt;

/// <summary>The stored metadata and attempt reference of one collaboration message, classified by the database without
/// materializing a stored enum. Provenance and kind use 0 for an unknown value; role and provider use 0 for a null column and 99 for a
/// stored value not known. No text column is part of it.</summary>
internal sealed record CollaborationAttemptSource(
    long Sequence,
    Guid Id,
    bool VersionKnown,
    bool TypeKnown,
    int ProvenanceCode,
    int ActorKindCode,
    int ActorRoleCode,
    int ActorProviderCode,
    Guid? AttemptId);
