using DevalCopilot.Api.Features.Runs.Contracts;
using DevalCopilot.Api.Features.Runs.GetAgentAttemptStatus;

namespace DevalCopilot.Api.Features.Runs.GetVerificationDiagnosisStatus;

/// <summary>Bounded status, findings, escalation, correction, and budget facts of the latest verification diagnosis, plus a
/// display hint about whether the current verification can be diagnosed. <c>HasAttempt: false</c> means this run has never
/// requested a diagnosis. <c>CorrectionDirectGuidance</c> is the direct human guidance recorded on the latest diagnosis-origin
/// correction attempt (<c>NotRecorded</c>, <c>Provided</c> with its accepted text, or <c>Unknown</c>), null when no correction exists;
/// it states what the host supplied to that attempt's sealed context, never that a provider followed it, and is separate from the
/// diagnosis and any escalation. Never a path, prompt, transcript, output, or credential.</summary>
public sealed record VerificationDiagnosisStatusResponse(
    bool HasAttempt,
    Guid? AttemptId,
    int? AttemptNumber,
    Guid? ExecutionReportMessageId,
    string? Status,
    string? Outcome,
    DateTimeOffset? ClaimedAtUtc,
    DateTimeOffset? DispatchedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    IReadOnlyList<AgentAttemptArtifactMetadataResponse> Artifacts,
    IReadOnlyList<VerificationDiagnosisMemberResponse> Verification,
    int FindingCount,
    Guid? DiagnosisEscalationMessageId,
    bool CorrectionApplicable,
    Guid? CorrectionAttemptId,
    int? CorrectionAttemptNumber,
    string? CorrectionStatus,
    string? CorrectionOutcome,
    Guid? ReviewableExecutionReportMessageId,
    int MaximumReviewCorrectionAttempts,
    int ReviewCorrectionAttemptsUsed,
    bool CorrectionBudgetExhausted,
    Guid? CorrectionEscalationId,
    Guid? CorrectionEscalationMessageId,
    Guid? DiagnosableExecutionReportMessageId,
    string? DiagnosisUnavailableCode,
    AgentProcessExecutionResponse? ProcessExecution,
    AgentTokenUsageResponse? TokenUsage,
    string? ConfiguredCommandSandbox,
    string? ConfiguredRolloutPersistence,
    DirectHumanGuidanceResponse? CorrectionDirectGuidance);
