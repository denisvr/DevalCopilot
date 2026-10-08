using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.RequestLocalCommit;

/// <summary>
/// The one explicit request to commit a run's exact verified, Agent-reviewed and human-approved checkpoint locally (ADR-0029). It
/// pins the operation, checkpoint, approved CodeReviewer attempt, approved human review and the human-written message; the host
/// derives every path, ref, SHA and argument. Manual transaction: Git preparation runs outside any transaction and the handler
/// owns the short write-locked admission transaction.
/// </summary>
public sealed record RequestLocalCommitCommand(
    Guid RunId,
    Guid OperationId,
    Guid CheckpointId,
    Guid CodeReviewAttemptId,
    Guid HumanCheckpointReviewId,
    string Message) : IManualTransactionCommand<Result<LocalCommitOperationView>>;
