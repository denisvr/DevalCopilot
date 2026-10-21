using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;

namespace DevalCopilot.Application.Features.Runs.Commands.ResolveLocalExecutionSequenceStepPreparation;

/// <summary>
/// Settles a step whose one claim attempt ended without a success the caller can rely on (a refusal, an exception or a cancellation)
/// (ADR-0060). It never retries: the exact step/operation association is read back through an independent connection. A claim that
/// proved persisted changes nothing and the sequence continues by observation; an absent claim revokes the step and ends the sequence
/// as NeedsAttention with the original stable reason preserved; an unresolved claim marks the step unresolved, ends the sequence as
/// NeedsAttention and preserves every possibly referenced artifact. Absence or uncertainty never authorizes another claim.
/// Manual transaction: the independent read must complete before the short write-locked transaction begins.
/// </summary>
public sealed record ResolveLocalExecutionSequenceStepPreparationCommand(LocalExecutionStepTicket Ticket, string? FailureCode)
    : IManualTransactionCommand<Result<ResolveLocalExecutionSequenceStepPreparationCommandResult>>;
