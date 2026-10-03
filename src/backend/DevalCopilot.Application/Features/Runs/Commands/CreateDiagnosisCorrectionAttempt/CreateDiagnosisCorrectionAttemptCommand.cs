using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.CreateDiagnosisCorrectionAttempt;

/// <summary>
/// Claims one durable Implementer correction attempt (the existing ReviewCorrection contract and Claude correction adapter)
/// for the exact completed verification diagnosis identified by <paramref name="VerificationDiagnosisAttemptId"/>
/// (ADR-0018), or records the diagnosis's one durable human escalation at budget exhaustion. The ordinary
/// <see cref="CreateReviewCorrectionAttemptCommand"/> keeps its review-source contract; this command shares only the
/// ReviewCorrection contract and its shared budget, and has no authorization input. Optional
/// <paramref name="Guidance"/> is the advisory direct human guidance of ADR-0015, extended to this request by ADR-0019:
/// <see langword="null"/> is exactly the unguided request, and supplied text is available only within the shared
/// correction allowance.
/// </summary>
public sealed record CreateDiagnosisCorrectionAttemptCommand(
    Guid RunId, Guid VerificationDiagnosisAttemptId, string? Guidance = null)
    : IManualTransactionCommand<Result<CreateDiagnosisCorrectionAttemptCommandResult>>;
