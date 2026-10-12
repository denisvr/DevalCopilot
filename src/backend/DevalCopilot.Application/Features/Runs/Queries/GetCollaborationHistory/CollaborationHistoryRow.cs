namespace DevalCopilot.Application.Features.Runs.Queries.GetCollaborationHistory;

/// <summary>The database-classified projection of one message shared by the history page and the reply inspection, so both admit a
/// message identically. Every stored enum is classified by the database and never materialized: type, provenance and kind use 0 for an
/// unknown value; role and provider use 0 for a null column and 99 for a stored value not known. The two text columns come back null
/// past the Domain byte bounds, so an oversized value is never read.</summary>
internal sealed record CollaborationHistoryRow(
    long Sequence,
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    bool VersionKnown,
    int TypeCode,
    int ProvenanceCode,
    int ActorKindCode,
    int ActorRoleCode,
    int ActorProviderCode,
    int RecipientKindCode,
    int RecipientRoleCode,
    int RecipientProviderCode,
    string? Summary,
    string? StructuredContentJson);
