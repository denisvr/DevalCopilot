using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetClaudeCriticalReviewAttemptStatus;

/// <summary>Bounded status and artifact metadata for the most recent Claude critical-review
/// attempt on a run — never a path, prompt, transcript, or credential. An unknown run fails with
/// <c>runs.not_found</c>; an existing run with no such attempt yet succeeds with an explicit
/// <c>HasAttempt: false</c> result — never an ambiguous null body. Mirrors
/// <c>GetAgentAttemptStatusQuery</c> exactly, restricted to CriticalReviewer attempts.</summary>
public sealed record GetClaudeCriticalReviewAttemptStatusQuery(Guid RunId)
    : IQuery<Result<ClaudeCriticalReviewAttemptStatusQueryResult>>;
