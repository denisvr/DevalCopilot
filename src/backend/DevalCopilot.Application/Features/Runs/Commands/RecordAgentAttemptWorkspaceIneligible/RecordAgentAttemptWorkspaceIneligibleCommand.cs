using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordAgentAttemptWorkspaceIneligible;

/// <summary>
/// Records that an Agent attempt's Ready workspace, active mutation lease, or current checkpoint
/// no longer holds — detected before dispatch by <c>GetIneligibleAgentAttemptsQuery</c>. The
/// provider is never invoked for this outcome. Distinct from
/// <c>RecordAgentAttemptSourceChangedCommand</c>: this is about workspace/lease state, not a Git
/// fingerprint mismatch.
/// </summary>
public sealed record RecordAgentAttemptWorkspaceIneligibleCommand(Guid RunId, Guid AttemptId) : ICommand<Result>;
