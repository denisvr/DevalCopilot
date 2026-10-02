using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.CreateDiagnosisCorrectionAttempt;

/// <summary>
/// Claims one durable Implementer correction attempt (the existing ReviewCorrection contract and Claude correction adapter)
/// for the exact completed verification diagnosis identified by <paramref name="VerificationDiagnosisAttemptId"/>
/// (ADR-0018), or records the diagnosis's one durable human escalation at budget exhaustion. The ordinary
/// <see cref="CreateReviewCorrectionAttemptCommand"/> keeps its review-source contract; this command shares only the
/// ReviewCorrection contract and its shared budget, and has no authorization or guidance input.
/// </summary>
public sealed record CreateDiagnosisCorrectionAttemptCommand(Guid RunId, Guid VerificationDiagnosisAttemptId)
    : IManualTransactionCommand<Result<CreateDiagnosisCorrectionAttemptCommandResult>>;
