using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Projects.Commands.RecordLocalExecutionSequenceVerificationDispatchRefusal;

/// <summary>
/// Records, for a still-undispatched verification execution linked to a local execution sequence, the dispatch-gate loss a supervisor
/// observed (ADR-0060). The handler never trusts the caller: under the write lock it independently proves the exact linked, Running,
/// undispatched execution and that its sequence authority, deadline, source or pinned recipes no longer permit dispatch, and only then
/// records the dedicated terminal status with no process outcome, exit code or completion fingerprint. It writes no protocol result and
/// refunds nothing. Manual transaction: the proof and the write share one short write-locked transaction.
/// </summary>
public sealed record RecordLocalExecutionSequenceVerificationDispatchRefusalCommand(Guid ProjectId, Guid ExecutionId)
    : IManualTransactionCommand<Result>;
