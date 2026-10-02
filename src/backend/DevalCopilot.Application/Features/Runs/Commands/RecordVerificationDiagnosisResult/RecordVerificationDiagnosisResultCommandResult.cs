using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordVerificationDiagnosisResult;

/// <summary>The recorded terminal status and outcome, and the latest durable event sequence. The outcome may differ from the
/// one reported when the handler's fresh applicability check classified the response as stale.</summary>
public sealed record RecordVerificationDiagnosisResultCommandResult(AttemptStatus Status, AgentOutcome Outcome, long LatestEventSequence);
