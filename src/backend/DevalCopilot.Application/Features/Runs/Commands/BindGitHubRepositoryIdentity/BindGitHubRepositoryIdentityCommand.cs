using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.BindGitHubRepositoryIdentity;

/// <summary>
/// The one explicit Human confirmation of the opaque repository node identifier of a run's recorded ADR-0040 target (ADR-0043). The caller
/// supplies only the operation, the exact lowercase commit, the target identifier and the node identifier it confirmed; the host resolves
/// the pinned scope, observes the identity itself exactly once and records the binding only when that fresh observation agrees. It is
/// historical identity evidence: never permission, branch or commit proof, CI approval, publication authority or a project-wide alias.
/// Manual transaction: the handler owns the short write-locked transaction that decides and records the binding and its one event together;
/// the external observation never runs inside it.
/// </summary>
public sealed record BindGitHubRepositoryIdentityCommand(
    Guid RunId, Guid OperationId, string CommitSha, Guid TargetId, string RepositoryNodeId)
    : IManualTransactionCommand<Result<BindGitHubRepositoryIdentityCommandResult>>;
