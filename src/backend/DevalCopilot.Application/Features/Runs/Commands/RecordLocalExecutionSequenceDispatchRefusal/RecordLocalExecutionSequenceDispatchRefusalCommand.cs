using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordLocalExecutionSequenceDispatchRefusal;

/// <summary>
/// Records, for a still-undispatched linked attempt of a local execution sequence, the dispatch-gate loss a supervisor observed
/// (ADR-0060). The handler never trusts the caller: under the write lock it independently proves the exact linked, Running, undispatched
/// implementation, review, correction or diagnosis attempt and that its sequence authority, deadline, pinned source or pinned set no longer
/// permits dispatch, and only then records the dedicated outcome with no process evidence. It invokes no provider, writes no protocol
/// output and refunds no budget. Manual transaction: the proof and the write share one short write-locked transaction.
/// </summary>
public sealed record RecordLocalExecutionSequenceDispatchRefusalCommand(Guid RunId, Guid AttemptId) : IManualTransactionCommand<Result>;
