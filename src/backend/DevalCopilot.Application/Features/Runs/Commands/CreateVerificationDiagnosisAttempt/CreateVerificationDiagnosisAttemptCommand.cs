using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;

namespace DevalCopilot.Application.Features.Runs.Commands.CreateVerificationDiagnosisAttempt;

/// <summary>
/// Claims one durable Codex verification-diagnosis attempt (ADR-0018) for the current failed local verification of exactly
/// one explicit, already-recorded, provider-observed Claude ExecutionReport. The host derives the complete verification
/// selection; the caller supplies only the report. Manual transaction: the Git evidence capture, output verification, and
/// artifact sealing must never run inside the mediator's automatic per-command transaction, and the handler opens its own
/// short guard transaction only after all external work completes.
/// </summary>
public sealed record CreateVerificationDiagnosisAttemptCommand(Guid RunId, Guid ExecutionReportMessageId)
    : IManualTransactionCommand<Result<CreateVerificationDiagnosisAttemptCommandResult>>
{
    /// <summary>The exact local execution sequence step (ADR-0060) this claim is composed for; never bound from a request.</summary>
    internal LocalExecutionStepTicket? LocalStep { get; init; }
}
