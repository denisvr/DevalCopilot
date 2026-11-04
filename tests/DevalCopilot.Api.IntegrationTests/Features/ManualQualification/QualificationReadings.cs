using DevalCopilot.Api.IntegrationTests.ManualQualification;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

/// <summary>Coherent readings of the two stages, for the controls to start from and to corrupt one fact at a time.</summary>
public static class QualificationReadings
{
    private const EvidenceRouteComparisonStatus Agrees = EvidenceRouteComparisonStatus.Agrees;

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
        AdapterContractVersion = QualificationContracts.PlannerAdapter,
        Status = "Completed",
        Outcome = "Proposed",
        Dispatched = true,
        ProcessOutcome = "Exited",
        ExitCode = 0,
        StandardOutputCompletion = "Complete",
        StandardErrorCompletion = "Complete",
        StandardInputDelivery = "Complete",
        DurationMilliseconds = 1234,
        StatusRouteAgrees = true,
        AttemptEvidenceRouteComparison = EvidenceRouteComparisonStatus.Agrees,
        MessageCount = 1,
        Messages = [new StageMessage(
            proposalMessageId, "Proposal", null, attemptId, "Codex.Planner", "ProviderObserved", Agrees, Agrees)],
        MessageSetComparison = Agrees,
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
        AdapterContractVersion = QualificationContracts.ReviewerAdapter,
        Status = "Completed",
        Outcome = accepted ? "Accepted" : "Challenged",
        Dispatched = true,
        ProcessOutcome = "Exited",
        ExitCode = 0,
        StandardOutputCompletion = "Complete",
        StandardErrorCompletion = "Complete",
        StandardInputDelivery = "Complete",
        DurationMilliseconds = 4321,
        StatusRouteAgrees = true,
        AttemptEvidenceRouteComparison = EvidenceRouteComparisonStatus.Agrees,
        ReviewedProposalMessageId = proposalMessageId,
        MessageCount = 1,
        Messages = [Reply(attemptId, replyMessageId, proposalMessageId, accepted)],
        MessageSetComparison = Agrees,
        InputMessageIds = [proposalMessageId],
        ManifestPresent = true,
        ManifestBytesAgree = true,
        ManifestContractAgrees = true,
        ManifestObjectiveAgrees = true,
        ManifestProposalMessageId = proposalMessageId,
        ArtifactCount = 4,
        ArtifactsAgreeing = 4,
    };

    /// <summary>A coherent Challenge reading with a set of the given size, each reply a distinct message of the accepted attempt.</summary>
    public static StageReading ChallengeSet(Guid attemptId, Guid proposalMessageId, int size) =>
        Reviewer(attemptId, Guid.NewGuid(), proposalMessageId) with
        {
            MessageCount = size,
            Messages = [.. Enumerable.Range(0, size).Select(_ => Reply(attemptId, Guid.NewGuid(), proposalMessageId, accepted: false))],
        };

    /// <summary>One coherent reply of the reviewer: an Acceptance or a Challenge to the Proposal, its two comparisons agreeing.</summary>
    public static StageMessage Reply(Guid attemptId, Guid replyMessageId, Guid proposalMessageId, bool accepted) => new(
        replyMessageId,
        accepted ? "Acceptance" : "Challenge",
        proposalMessageId,
        attemptId,
        "ClaudeCode.CriticalReviewer",
        "ProviderObserved",
        Agrees,
        Agrees);

    /// <summary>The reading with every listed message changed by the same edit, whatever their number.</summary>
    public static StageReading EditEach(this StageReading reading, Func<StageMessage, StageMessage> edit) =>
        reading with { Messages = [.. reading.Messages.Select(edit)] };

    /// <summary>The reading with only the message at the position changed.</summary>
    public static StageReading EditAt(this StageReading reading, int position, Func<StageMessage, StageMessage> edit) =>
        reading with { Messages = [.. reading.Messages.Select((message, index) => index == position ? edit(message) : message)] };

    /// <summary>The reading as of an attempt that was observed to produce no message and to have none stored: an observed zero, an
    /// empty list and an empty set that equals the empty stored set.</summary>
    public static StageReading WithoutMessages(this StageReading reading) =>
        reading with { MessageCount = 0, Messages = [], MessageSetComparison = Agrees };

    /// <summary>The reading as of a timeline that was never enumerated: no count and no list, which is not a zero.</summary>
    public static StageReading WithUnenumeratedMessages(this StageReading reading) =>
        reading with { MessageCount = null, Messages = [], MessageSetComparison = EvidenceRouteComparisonStatus.NotAttempted };

    public static SourceSnapshot Snapshot(string suffix = "") =>
        new(Commit, "refs/heads/main", $"refs/heads/main {Commit}\n", $"100644 {suffix}\n", "f" + suffix);
}
