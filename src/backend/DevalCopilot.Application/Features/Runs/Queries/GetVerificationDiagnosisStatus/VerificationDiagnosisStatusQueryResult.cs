using DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptStatus;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetVerificationDiagnosisStatus;

/// <param name="HasAttempt">The explicit discriminator for "this run has never requested a diagnosis"; every attempt field is
/// then null/empty.</param>
/// <param name="DiagnosableExecutionReportMessageId">A display hint only: the current valid ExecutionReport when the complete
/// current verification selection is diagnosable (a coherent mix of Passed and Failed with at least one failure) and has no
/// successful diagnosis yet. The host re-decides everything on a request.</param>
/// <param name="DiagnosisUnavailableCode">A fixed code naming why the current verification cannot be diagnosed, or null.</param>
/// <param name="CorrectionApplicable">Whether the latest diagnosis recorded findings that still exactly apply: its report
/// chain, its complete findings, and the verification evidence it was produced against are unchanged.</param>
/// <param name="ReviewableExecutionReportMessageId">The corrected ExecutionReport when the latest diagnosis-origin correction
/// completed as CorrectionApplied and is still the workspace's current result; the ordinary code review of it requires new
/// Passed verification.</param>
public sealed record VerificationDiagnosisStatusQueryResult(
    bool HasAttempt,
    Guid? AttemptId,
    int? AttemptNumber,
    Guid? ExecutionReportMessageId,
    AttemptStatus? Status,
    AgentOutcome? Outcome,
    DateTimeOffset? ClaimedAtUtc,
    DateTimeOffset? DispatchedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    IReadOnlyList<AgentAttemptArtifactMetadata> Artifacts,
    IReadOnlyList<VerificationDiagnosisMember> Verification,
    int FindingCount,
    Guid? DiagnosisEscalationMessageId,
    bool CorrectionApplicable,
    Guid? CorrectionAttemptId,
    int? CorrectionAttemptNumber,
    AttemptStatus? CorrectionStatus,
    AgentOutcome? CorrectionOutcome,
    Guid? ReviewableExecutionReportMessageId,
    int MaximumReviewCorrectionAttempts,
    int ReviewCorrectionAttemptsUsed,
    bool CorrectionBudgetExhausted,
    Guid? CorrectionEscalationId,
    Guid? CorrectionEscalationMessageId,
    Guid? DiagnosableExecutionReportMessageId,
    string? DiagnosisUnavailableCode,
    AgentProcessExecutionEvidence? ProcessExecution = null,
    TimeSpan? Timeout = null,
    AgentTokenUsageEvidence? TokenUsage = null,
    string? ConfiguredCommandSandbox = null,
    string? ConfiguredRolloutPersistence = null)
{
    public static VerificationDiagnosisStatusQueryResult NoAttempt(
        int maximumReviewCorrectionAttempts,
        int reviewCorrectionAttemptsUsed,
        Guid? diagnosableExecutionReportMessageId,
        string? diagnosisUnavailableCode) =>
        new(
            false, null, null, null, null, null, null, null, null, [], [], 0, null, false, null, null, null, null, null,
            maximumReviewCorrectionAttempts, reviewCorrectionAttemptsUsed, reviewCorrectionAttemptsUsed >= maximumReviewCorrectionAttempts,
            null, null, diagnosableExecutionReportMessageId, diagnosisUnavailableCode);
}
