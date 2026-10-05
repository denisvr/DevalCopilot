using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs.Policies;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordCodexAccountUsageStop;

/// <summary>
/// Resolves a claimed, never-dispatched Codex attempt that took a run-scoped account-usage stop to its one terminal pre-dispatch
/// outcome (ADR-0025): the bounded canonical decision and the completion event are recorded atomically, with no dispatch marker, no
/// provider invocation, no collaboration message and no checkpoint review. The claim's budgets and consumed authorizations stay
/// spent. <paramref name="Facts"/> are the supervisor's own guard facts for this attempt (null when no observation could be made);
/// the handler never trusts them: it re-validates the persisted attempt, its stored threshold snapshot and the current vetted launch
/// tuple, re-evaluates the policy itself, and refuses to stop an attempt whose evidence the policy permits. Declared
/// <see cref="IManualTransactionCommand{TResult}"/>: the fresh authority read, the single mutation and save, and the commit are one
/// short explicit transaction (the committed event sequence is needed for the notification that follows the commit), with no external
/// work inside it. Internal to the host: no HTTP contract reaches it.
/// </summary>
public sealed record RecordCodexAccountUsageStopCommand(Guid RunId, Guid AttemptId, CodexAccountUsageGuardFacts? Facts)
    : IManualTransactionCommand<Result<RecordCodexAccountUsageStopCommandResult>>;
