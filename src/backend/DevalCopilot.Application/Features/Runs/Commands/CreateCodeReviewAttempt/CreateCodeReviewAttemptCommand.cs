using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.CreateCodeReviewAttempt;

/// <summary>
/// Claims one durable Codex code-review attempt for an eligible run, reviewing exactly one
/// explicit, already-recorded, provider-observed Claude implementation ExecutionReport. Mirrors
/// <c>CreateClaudeCriticalReviewAttemptCommand</c>/<c>CreateChallengeResolutionAttemptCommand</c>.
/// </summary>
public sealed record CreateCodeReviewAttemptCommand(Guid RunId, Guid ExecutionReportMessageId)
    : ICommand<Result<CreateCodeReviewAttemptCommandResult>>;
