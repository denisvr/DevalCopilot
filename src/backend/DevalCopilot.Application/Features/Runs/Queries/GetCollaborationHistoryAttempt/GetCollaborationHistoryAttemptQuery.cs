using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetCollaborationHistoryAttempt;

/// <summary>
/// The recorded Agent-attempt attribution of exactly one recorded collaboration message of a run (ADR-0037). It reads the source's own
/// stored metadata and attempt reference (never its summary or content JSON) and resolves at most that one attempt, in the same run,
/// through bounded scalar projections; it never substitutes a latest, same-role or same-number attempt and never follows repairs,
/// inputs or reply parents. It writes, claims and repairs nothing. An unknown run or a missing or foreign source fails with
/// <c>collaboration_attempt.not_found</c>.
/// </summary>
public sealed record GetCollaborationHistoryAttemptQuery(Guid RunId, Guid MessageId)
    : IQuery<Result<GetCollaborationHistoryAttemptQueryResult>>;
