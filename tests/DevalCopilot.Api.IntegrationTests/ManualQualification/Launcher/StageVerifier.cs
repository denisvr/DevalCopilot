namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// Decides whether one stage's reading is a genuine, coherent, durably recorded result of the role the owner authorized, bound to
/// the exact Proposal when it is the review. It answers with the first failing closed code, or null. Nothing here forces an
/// Acceptance: a complete set of one to five Challenges, every one replying to the exact Proposal, is equally valid. One Planner
/// attempt records exactly one Proposal; one CriticalReviewer attempt records either exactly one Acceptance or the complete set of
/// Challenges its recorded outcome names, never a mixture, a repeat, an excess or a reply that is not its own.
/// </summary>
public static class StageVerifier
{
    /// <summary>The first failing closed code, or null. A reading of any attempt other than the one the stage's POST was accepted for
    /// is foreign and can never be valid, however coherent it is.</summary>
    public static string? Verify(
        AllowanceRole role,
        StageReading reading,
        Guid? proposalMessageId,
        Guid acceptedAttemptId)
    {
        if (reading.AttemptFound && reading.AttemptId != acceptedAttemptId)
        {
            return "AttemptIdentityMismatch";
        }

        return role == AllowanceRole.Planner ? VerifyPlanner(reading) : VerifyReviewer(reading, proposalMessageId);
    }

    private static string? VerifyPlanner(StageReading reading)
    {
        if (!reading.AttemptFound)
        {
            return "NoAttempt";
        }

        return CommonProblem(
                reading, "Codex", "Planner", "Proposal", QualificationContracts.PlannerAdapter, expectedAttempts: 1)
            ?? (reading.Outcome == "Proposed" ? null : $"Attempt{reading.Status}{reading.Outcome ?? "None"}")
            ?? ProcessProblem(reading)
            ?? MessageSetProblem(reading, exactlyOne: true)
            ?? (reading.Messages.All(message => message is { Type: "Proposal", InReplyToMessageId: null })
                ? null
                : "ProposalNotRecorded")
            ?? (reading.Messages.All(message => message is { Actor: "Codex.Planner", Provenance: "ProviderObserved" })
                ? null
                : "ProposalAuthorMismatch")
            ?? (reading.InputMessageIds.Count == 0 ? null : "UnexpectedInputs")
            ?? EvidenceProblem(reading)
            ?? SealedProblem(reading);
    }

    private static string? VerifyReviewer(StageReading reading, Guid? proposalMessageId)
    {
        if (!reading.AttemptFound)
        {
            return "NoAttempt";
        }

        var expectedType = reading.Outcome switch
        {
            "Accepted" => "Acceptance",
            "Challenged" => "Challenge",
            _ => null,
        };
        return CommonProblem(
                reading,
                "ClaudeCode",
                "CriticalReviewer",
                "CriticalReview",
                QualificationContracts.ReviewerAdapter,
                expectedAttempts: 2)
            ?? (expectedType is not null ? null : $"Attempt{reading.Status}{reading.Outcome ?? "None"}")
            ?? ProcessProblem(reading)
            ?? (proposalMessageId is not null && reading.ReviewedProposalMessageId == proposalMessageId
                ? null
                : "ReviewedProposalMismatch")
            ?? MessageSetProblem(reading, exactlyOne: false)
            ?? (reading.Messages.Select(message => message.Type).Distinct().Count() <= 1 ? null : "MixedReplySet")
            ?? (reading.Messages.All(message => message.Type == expectedType) ? null : "ReplyNotRecorded")
            ?? (reading.Messages.All(message => message.InReplyToMessageId == proposalMessageId)
                ? null
                : "ReplyLineageMismatch")
            ?? (reading.Messages.All(message =>
                    message is { Actor: "ClaudeCode.CriticalReviewer", Provenance: "ProviderObserved" })
                ? null
                : "ReplyAuthorMismatch")
            ?? (reading.InputMessageIds.SequenceEqual([proposalMessageId!.Value]) ? null : "InputSetMismatch")
            ?? EvidenceProblem(reading)
            ?? SealedProblem(reading)
            ?? (reading.ManifestProposalMessageId == proposalMessageId ? null : "SealedProposalMismatch");
    }

    private static string? CommonProblem(
        StageReading reading,
        string provider,
        string role,
        string contract,
        string adapterContractVersion,
        int expectedAttempts)
    {
        if (reading.Provider != provider || reading.Role != role || reading.Contract != contract)
        {
            return "WrongRoleOrProvider";
        }

        if (reading.AdapterContractVersion != adapterContractVersion)
        {
            return "AdapterContractNotCurrent";
        }

        if (reading.AttemptsInRun != expectedAttempts || reading.DispatchedAttemptsInRun > expectedAttempts)
        {
            return "UnexpectedAttemptCount";
        }

        return reading.Status == "Completed" ? null : $"Attempt{reading.Status}{reading.Outcome ?? "None"}";
    }

    private static string? ProcessProblem(StageReading reading)
    {
        if (!reading.Dispatched || reading.ProcessOutcome != "Exited" || reading.ExitCode != 0)
        {
            return "ProcessNotCleanExit";
        }

        if (!reading.RequiredIoComplete)
        {
            return "RequiredIoIncomplete";
        }

        if (!reading.StatusRouteAgrees)
        {
            return "StatusRouteDisagrees";
        }

        if (!reading.AttemptEvidenceRouteAgrees)
        {
            return "AttemptEvidenceRouteDisagrees";
        }

        return reading.InvocationDiagnosticState == "NotRecorded" ? null : "UnexpectedInvocationDiagnostic";
    }

    /// <summary>The produced messages of the accepted attempt as one complete set: counted and bounded by the role and recorded
    /// outcome (one Proposal, one Acceptance, or one to five Challenges), every one of them the attempt's own, no identity repeated, and
    /// the complete distinct timeline set equal to the stored message rows with each row agreeing. An unobserved count is a missing
    /// message, never an empty valid set.</summary>
    private static string? MessageSetProblem(StageReading reading, bool exactlyOne)
    {
        var maximum = StageMessageBounds.Maximum(reading.Role, reading.Outcome);
        if (reading.MessageCount is not { } count || count < 1)
        {
            return "MessageNotRecorded";
        }

        if (count > maximum)
        {
            return exactlyOne ? "MessageNotRecorded" : "UnexpectedMessageCount";
        }

        if (reading.Messages.Count != count || reading.Messages.Any(message => message.AttemptId != reading.AttemptId))
        {
            return "MessageNotRecorded";
        }

        if (reading.Messages.Select(message => message.Id).Distinct().Count() != count)
        {
            return "DuplicateMessage";
        }

        var stored = reading.MessageSetComparison == EvidenceRouteComparisonStatus.Agrees
            && reading.Messages.All(message => message.PersistedMetadataComparison == EvidenceRouteComparisonStatus.Agrees);
        return stored ? null : "MessageSetMismatch";
    }

    /// <summary>The public message-evidence route was compared with the stored attempt for every message of the set, and every
    /// comparison agreed. A message that was never compared is not a message that agreed.</summary>
    private static string? EvidenceProblem(StageReading reading)
    {
        if (reading.Messages.Count > 0 && reading.Messages.All(
                message => message.EvidenceRouteComparison == EvidenceRouteComparisonStatus.Agrees))
        {
            return null;
        }

        // A request that failed is reported as such, unless another reply is already known to disagree.
        var failed = reading.Messages.Any(message => message.EvidenceRouteComparison == EvidenceRouteComparisonStatus.Unavailable)
            && reading.Messages.All(message => message.EvidenceRouteComparison != EvidenceRouteComparisonStatus.Disagrees);
        return failed ? "EvidenceRouteUnavailable" : "EvidenceRouteDisagrees";
    }

    private static string? SealedProblem(StageReading reading)
    {
        if (!reading.ManifestPresent || !reading.ManifestBytesAgree)
        {
            return "SealedInputMismatch";
        }

        if (!reading.ManifestContractAgrees || !reading.ManifestObjectiveAgrees)
        {
            return "SealedInputContent";
        }

        var everyArtifactAgrees = reading.ArtifactCount > 0 && reading.ArtifactsAgreeing == reading.ArtifactCount;
        return everyArtifactAgrees ? null : "ArtifactMismatch";
    }
}
