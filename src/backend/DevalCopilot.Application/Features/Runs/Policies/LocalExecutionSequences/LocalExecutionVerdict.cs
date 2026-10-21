using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;

/// <summary>The one decision a coordinator observation derives from a terminal linked operation (ADR-0060).</summary>
internal sealed record LocalExecutionVerdict(
    LocalExecutionVerdictKind Kind,
    LocalExecutionStepKind NextKind = LocalExecutionStepKind.Implementation,
    int PassNumber = 0,
    int? RecipeOrdinal = null,
    Guid? SourceCheckpointId = null,
    string? SourceFingerprintSha256 = null,
    Guid? ReportMessageId = null,
    Guid? SubjectAttemptId = null,
    Guid? ReviewAttemptId = null,
    Guid? EscalationMessageId = null,
    string? Reason = null,
    string? Detail = null)
{
    public static LocalExecutionVerdict Next(
        LocalExecutionStepKind kind,
        int pass,
        int? recipeOrdinal,
        Guid sourceCheckpointId,
        string sourceFingerprintSha256,
        Guid reportMessageId,
        Guid? subjectAttemptId = null) => new(
        LocalExecutionVerdictKind.Reserve,
        kind,
        pass,
        recipeOrdinal,
        sourceCheckpointId,
        sourceFingerprintSha256,
        reportMessageId,
        subjectAttemptId);

    public static LocalExecutionVerdict Approved(Guid reportMessageId, Guid reviewAttemptId) =>
        new(LocalExecutionVerdictKind.Approved, ReportMessageId: reportMessageId, ReviewAttemptId: reviewAttemptId);

    public static LocalExecutionVerdict AllowanceReached(Guid reportMessageId) =>
        new(LocalExecutionVerdictKind.AllowanceReached, ReportMessageId: reportMessageId);

    public static LocalExecutionVerdict ProviderEscalation(Guid reportMessageId, Guid escalationMessageId) =>
        new(LocalExecutionVerdictKind.ProviderEscalation, ReportMessageId: reportMessageId, EscalationMessageId: escalationMessageId);

    public static LocalExecutionVerdict Attention(string reason, string? detail = null) =>
        new(LocalExecutionVerdictKind.Attention, Reason: reason, Detail: detail);
}
