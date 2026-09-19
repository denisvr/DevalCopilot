using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.ReconcileInterruptedImplementationAttempts;

/// <summary>
/// The Implementer-specific restart-reconciliation command, run once at startup alongside
/// <c>ReconcileInterruptedAgentAttemptsCommand</c> (which deliberately excludes this role). Unlike
/// every other Agent role, a dispatched implementation attempt may have mutated the owned
/// worktree before the host was lost — this handler independently re-reads fresh Git evidence
/// outside any EF transaction for each affected workspace before ever deciding whether it must
/// also be flagged <see cref="Domain.Features.Projects.WorkspaceStatus.NeedsAttention"/>. Never
/// guesses success, and never marks NeedsAttention for an attempt that was never dispatched (the
/// provider was never invoked, so the worktree cannot have been mutated by it).
/// </summary>
public sealed record ReconcileInterruptedImplementationAttemptsCommand : IManualTransactionCommand<Result<int>>;
