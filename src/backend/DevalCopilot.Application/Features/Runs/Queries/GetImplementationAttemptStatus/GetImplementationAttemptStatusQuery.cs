using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetImplementationAttemptStatus;

/// <summary>Bounded status, checkpoint, changed-file, and artifact metadata for the most recent
/// Claude implementation attempt on a run — never a path, prompt, transcript, or credential. An
/// unknown run fails with <c>runs.not_found</c>; an existing run with no such attempt yet
/// succeeds with an explicit <c>HasAttempt: false</c> result — never an ambiguous null body.
/// Mirrors <c>GetChallengeResolutionAttemptStatusQuery</c> exactly, restricted to Implementer
/// attempts.</summary>
public sealed record GetImplementationAttemptStatusQuery(Guid RunId) : IQuery<Result<ImplementationAttemptStatusQueryResult>>;
