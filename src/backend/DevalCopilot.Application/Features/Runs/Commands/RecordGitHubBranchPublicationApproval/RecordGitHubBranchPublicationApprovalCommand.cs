using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordGitHubBranchPublicationApproval;

/// <summary>
/// The one explicit Human approval of ONE future normal push of a run's exact recorded local commit to its exact recorded owned branch in
/// the repository named by its coherent ADR-0040 target and ADR-0043 binding (ADR-0048). The caller supplies only its own approval
/// identifier and the operation, commit, branch, target, binding and repository node identifier it acknowledged; the host resolves the
/// pinned scope itself and records the approval only when every acknowledgement equals it. It is recorded consent only: never readiness,
/// permission, a push or any other publication. Manual transaction: the handler owns the short write-locked transaction that decides and
/// records the approval and its one event together; nothing external runs.
/// </summary>
public sealed record RecordGitHubBranchPublicationApprovalCommand(
    Guid RunId,
    Guid ApprovalId,
    Guid OperationId,
    string CommitSha,
    string BranchName,
    Guid TargetId,
    Guid BindingId,
    string RepositoryNodeId)
    : IManualTransactionCommand<Result<RecordGitHubBranchPublicationApprovalCommandResult>>;
