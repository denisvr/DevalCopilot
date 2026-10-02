using Devalente.Shared.Cqrs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetEligibleVerificationDiagnosisAttempts;

public sealed record GetEligibleVerificationDiagnosisAttemptsQuery : IQuery<IReadOnlyList<EligibleVerificationDiagnosisAttempt>>;
