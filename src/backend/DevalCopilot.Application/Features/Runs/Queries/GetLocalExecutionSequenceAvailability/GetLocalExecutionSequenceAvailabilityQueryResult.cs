using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionSequenceAvailability;

/// <summary>The availability of one plan: either <see cref="Available"/> with the exact candidate and its confirmation value, or one
/// fixed <see cref="RefusalCode"/> and no candidate. It proves no runtime readiness.</summary>
public sealed record GetLocalExecutionSequenceAvailabilityQueryResult(
    bool Available,
    string? RefusalCode,
    Guid RunId,
    Guid PlanProposalMessageId,
    LocalExecutionPlanAuthority? PlanAuthority,
    Guid? WorkspaceId,
    Guid? CheckpointId,
    string? FingerprintSha256,
    string? ConfirmationSha256,
    int? CorrectionAllowance,
    int? MaximumSteps,
    IReadOnlyList<LocalExecutionSequenceAvailabilityRecipe>? Recipes,
    LocalExecutionSequenceAvailabilityDisclosure? Disclosure,
    LocalExecutionAdmissionMode AdmissionMode = LocalExecutionAdmissionMode.Immediate);
