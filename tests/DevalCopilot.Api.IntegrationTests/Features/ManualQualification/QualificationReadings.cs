using DevalCopilot.Api.IntegrationTests.ManualQualification;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

/// <summary>Coherent readings of the two stages, for the controls to start from and to corrupt one fact at a time.</summary>
public static class QualificationReadings
{
    private static readonly string Commit = new('a', 40);

    public static StageReading Planner(Guid attemptId, Guid proposalMessageId) => new()
    {
        AttemptsInRun = 1,
        DispatchedAttemptsInRun = 1,
        AttemptFound = true,
        AttemptId = attemptId,
        AttemptNumber = 1,
        Provider = "Codex",
        Role = "Planner",
        Contract = "Proposal",
        Status = "Completed",
        Outcome = "Proposed",
        Dispatched = true,
        ProcessOutcome = "Exited",
        ExitCode = 0,
        DurationMilliseconds = 1234,
        StatusRouteAgrees = true,
        MessageCount = 1,
        MessageId = proposalMessageId,
        MessageType = "Proposal",
        InReplyToMessageId = null,
        MessageAttemptId = attemptId,
        MessageActor = "Codex.Planner",
        MessageProvenance = "ProviderObserved",
        EvidenceRouteAgrees = true,
        InputMessageIds = [],
        ManifestPresent = true,
        ManifestBytesAgree = true,
        ManifestContractAgrees = true,
        ManifestObjectiveAgrees = true,
        ArtifactCount = 4,
        ArtifactsAgreeing = 4,
    };

    public static StageReading Reviewer(Guid attemptId, Guid replyMessageId, Guid proposalMessageId, bool accepted = false) => new()
    {
        AttemptsInRun = 2,
        DispatchedAttemptsInRun = 2,
        AttemptFound = true,
        AttemptId = attemptId,
        AttemptNumber = 2,
        Provider = "ClaudeCode",
        Role = "CriticalReviewer",
        Contract = "CriticalReview",
        Status = "Completed",
        Outcome = accepted ? "Accepted" : "Challenged",
        Dispatched = true,
        ProcessOutcome = "Exited",
        ExitCode = 0,
        DurationMilliseconds = 4321,
        StatusRouteAgrees = true,
        ReviewedProposalMessageId = proposalMessageId,
        MessageCount = 1,
        MessageId = replyMessageId,
        MessageType = accepted ? "Acceptance" : "Challenge",
        InReplyToMessageId = proposalMessageId,
        MessageAttemptId = attemptId,
        MessageActor = "ClaudeCode.CriticalReviewer",
        MessageProvenance = "ProviderObserved",
        EvidenceRouteAgrees = true,
        InputMessageIds = [proposalMessageId],
        ManifestPresent = true,
        ManifestBytesAgree = true,
        ManifestContractAgrees = true,
        ManifestObjectiveAgrees = true,
        ManifestProposalMessageId = proposalMessageId,
        ArtifactCount = 4,
        ArtifactsAgreeing = 4,
    };

    public static SourceSnapshot Snapshot(string suffix = "") =>
        new(Commit, "refs/heads/main", $"refs/heads/main {Commit}\n", $"100644 {suffix}\n", "f" + suffix);
}
