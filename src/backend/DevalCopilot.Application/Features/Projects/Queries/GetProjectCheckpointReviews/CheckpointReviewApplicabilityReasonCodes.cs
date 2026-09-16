namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectCheckpointReviews;

public static class CheckpointReviewApplicabilityReasonCodes
{
    public const string NewerCheckpoint = "review.newer_checkpoint";
    public const string SourceChanged = "review.source_changed";
    public const string CurrentEvidenceUnavailable = "review.current_evidence_unavailable";
    public const string NoCurrentCheckpoint = "review.no_current_checkpoint";
}
