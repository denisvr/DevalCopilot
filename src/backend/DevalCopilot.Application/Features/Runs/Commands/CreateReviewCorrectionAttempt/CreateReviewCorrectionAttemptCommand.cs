using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;

namespace DevalCopilot.Application.Features.Runs.Commands.CreateReviewCorrectionAttempt;

/// <summary>Claims one durable Implementer correction attempt for the exact completed
/// implementation review identified by <paramref name="ImplementationReviewAttemptId"/>.</summary>
public sealed record CreateReviewCorrectionAttemptCommand(
    Guid RunId, Guid ImplementationReviewAttemptId, string? Guidance = null)
    : IManualTransactionCommand<Result<CreateReviewCorrectionAttemptCommandResult>>
{
    /// <summary>The exact local execution sequence step (ADR-0060) this claim is composed for; never bound from a request.</summary>
    internal LocalExecutionStepTicket? LocalStep { get; init; }
}
