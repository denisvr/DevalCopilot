using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordGitHubObservationTarget;

/// <summary>
/// The one explicit Human selection of an immutable GitHub observation scope for the exact commit and branch of a completed manual
/// local delivery (ADR-0040). The caller supplies only the operation, the exact lowercase commit and the owner and repository; the
/// host derives the project, workspace and branch from pinned rows and fixes the host and purpose. It is local scope selection:
/// no remote, credential, process or provider is touched and nothing is verified or authorized. Manual transaction: the handler owns
/// the short write-locked transaction that decides and records the target and its one event together.
/// </summary>
public sealed record RecordGitHubObservationTargetCommand(Guid RunId, Guid OperationId, string CommitSha, string Owner, string Repository)
    : IManualTransactionCommand<Result<RecordGitHubObservationTargetCommandResult>>;
