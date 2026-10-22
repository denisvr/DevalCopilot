namespace DevalCopilot.Api.Features.Runs.GetLocalExecutionSequenceAvailability;

/// <summary>The recorded arming availability of one explicitly selected plan (ADR-0060): the exact candidate with its confirmation
/// value, or one fixed refusal code with its fixed text and no candidate. It performs no Git observation and proves no runtime
/// readiness; arming always checks afresh. Thresholds are canonical Int64 strings.</summary>
public sealed record GetLocalExecutionSequenceAvailabilityResponse(
    Guid RunId,
    Guid PlanProposalMessageId,
    bool Available,
    string? RefusalCode,
    string? RefusalText,
    string? PlanAuthority,
    Guid? WorkspaceId,
    Guid? CheckpointId,
    string? FingerprintSha256,
    string? ConfirmationSha256,
    int? CorrectionAllowance,
    int? MaximumSteps,
    IReadOnlyList<LocalExecutionSequenceAvailabilityRecipeResponse>? Recipes,
    LocalExecutionSequenceAvailabilityDisclosureResponse? Disclosure,
    string AdmissionMode);
