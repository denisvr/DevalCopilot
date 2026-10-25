namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionOwnershipReleases;

/// <summary>The dispatch witness of the step, as stored. The private host launch identity and nonce are never selected. Codes: family
/// 1 Agent, 2 Verification; kind 1 to 6; contract 1 ImplementationReport, 2 ImplementationReview, 3 ReviewCorrection, 4
/// VerificationDiagnosis; 0 for absent or unknown.</summary>
internal sealed record LocalExecutionReleaseWitness(
    Guid? Id,
    Guid? SequenceId,
    Guid? RunId,
    Guid? ProjectId,
    Guid? WorkspaceId,
    Guid? StepId,
    int FamilyCode,
    Guid? AgentAttemptId,
    Guid? VerificationExecutionId,
    int KindCode,
    int? Ordinal,
    int? PassNumber,
    int? RecipeOrdinal,
    Guid? PredecessorStepId,
    Guid? SourceCheckpointId,
    string? SourceFingerprint,
    Guid? ReportMessageId,
    Guid? SubjectAttemptId,
    int ContractCode,
    Guid? RecipeCommandId,
    Guid? AdmissionId,
    DateTimeOffset? DispatchedAtUtc);
