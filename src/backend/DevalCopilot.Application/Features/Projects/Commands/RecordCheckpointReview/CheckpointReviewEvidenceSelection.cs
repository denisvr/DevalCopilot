using Devalente.Shared.Results;
using DevalCopilot.Domain.Features.Projects;

namespace DevalCopilot.Application.Features.Projects.Commands.RecordCheckpointReview;

/// <summary>
/// Decides, before any Git observation or write, which evidence form a review request uses and whether its shape is admissible. The
/// two forms are never merged and nothing is silently deduplicated or truncated: an ambiguous or malformed selection is refused with
/// a fixed message that never echoes a submitted value.
/// </summary>
internal static class CheckpointReviewEvidenceSelection
{
    public const int MaximumExecutions = 32;

    public const string AmbiguousCode = "reviews.evidence_forms_ambiguous";
    public const string InvalidCode = "reviews.evidence_selection_invalid";
    public const string SetRequiresHumanCode = "reviews.evidence_set_requires_human";
    public const string PendingCannotIncludeEvidenceCode = "reviews.pending_cannot_include_evidence";

    /// <summary>The requested execution identifiers in the order submitted (empty for a Pending review or a legacy decided
    /// request without evidence) and whether the set form was used.</summary>
    internal sealed record Selection(Error? Error, bool UsesSet, IReadOnlyList<Guid> ExecutionIds);

    public static Selection Resolve(RecordCheckpointReviewCommand command)
    {
        var set = command.VerificationExecutionIds;
        if (set is not null && command.VerificationExecutionId.HasValue)
        {
            return Refused(Error.Failure(
                AmbiguousCode, "Submit either one verification execution or a set of verification executions, not both."), usesSet: true);
        }

        if (set is null)
        {
            if (command.Decision == ReviewDecision.Pending && command.VerificationExecutionId.HasValue)
            {
                return Refused(PendingCannotIncludeEvidence(), usesSet: false);
            }

            return new Selection(null, false, command.VerificationExecutionId is { } single ? [single] : []);
        }

        if (command.ActorKind != ReviewActorKind.Human)
        {
            return Refused(Error.Failure(
                SetRequiresHumanCode, "A verification execution set can be submitted only for a Human review."), usesSet: true);
        }

        if (set.Count > MaximumExecutions || set.Any(id => id == Guid.Empty) || set.Distinct().Count() != set.Count)
        {
            return Refused(InvalidSelection(), usesSet: true);
        }

        if (command.Decision == ReviewDecision.Pending)
        {
            return set.Count > 0 ? Refused(PendingCannotIncludeEvidence(), usesSet: true) : new Selection(null, true, []);
        }

        return set.Count == 0 ? Refused(InvalidSelection(), usesSet: true) : new Selection(null, true, set.ToArray());
    }

    public static Error InvalidSelection() => Error.Failure(
        InvalidCode, "A verification execution set must contain 1 to 32 unique, nonempty execution identifiers.");

    private static Error PendingCannotIncludeEvidence() =>
        Error.Conflict(PendingCannotIncludeEvidenceCode, "A pending review cannot include verification evidence.");

    private static Selection Refused(Error error, bool usesSet) => new(error, usesSet, []);
}
