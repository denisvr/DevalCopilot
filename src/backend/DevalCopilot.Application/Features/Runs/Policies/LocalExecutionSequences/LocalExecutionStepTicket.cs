using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;

/// <summary>
/// The internal identity under which the coordinator composes one of the existing claim commands (ADR-0060). Its constructor is internal
/// to the Application and the claim commands expose it only through an internal initializer, so no MVC request, DTO or other assembly
/// can supply a sequence or step identity. The ticket carries identities only: every authority fact (the preparation token, deadline,
/// expected step, pinned source, pass membership and expected inputs) is re-read from the database at the claim's durable seam.
/// </summary>
public sealed class LocalExecutionStepTicket
{
    internal LocalExecutionStepTicket(
        Guid sequenceId,
        Guid stepId,
        int ordinal,
        LocalExecutionStepKind kind,
        Guid reservedOperationId,
        Guid ownerToken,
        Guid sourceCheckpointId,
        Guid? reportMessageId,
        Guid? subjectAttemptId,
        Guid? recipeId)
    {
        SequenceId = sequenceId;
        StepId = stepId;
        Ordinal = ordinal;
        Kind = kind;
        ReservedOperationId = reservedOperationId;
        OwnerToken = ownerToken;
        SourceCheckpointId = sourceCheckpointId;
        ReportMessageId = reportMessageId;
        SubjectAttemptId = subjectAttemptId;
        RecipeId = recipeId;
    }

    public Guid SequenceId { get; }

    public Guid StepId { get; }

    public int Ordinal { get; }

    public LocalExecutionStepKind Kind { get; }

    /// <summary>The operation identity (an Agent attempt or a verification execution) the claim must use, chosen once at reservation.</summary>
    public Guid ReservedOperationId { get; }

    internal Guid OwnerToken { get; }

    internal Guid SourceCheckpointId { get; }

    internal Guid? ReportMessageId { get; }

    internal Guid? SubjectAttemptId { get; }

    /// <summary>The pinned project verification recipe a verification step runs; null for every Agent step.</summary>
    internal Guid? RecipeId { get; }
}
