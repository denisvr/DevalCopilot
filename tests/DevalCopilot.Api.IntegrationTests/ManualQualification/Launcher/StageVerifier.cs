namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// Decides whether one stage's reading is a genuine, coherent, durably recorded result of the role the owner authorized, bound to
/// the exact Proposal when it is the review. It answers with the first failing closed code, or null. Nothing here forces an
/// Acceptance: a Challenge replying to the exact Proposal is equally valid.
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

        return CommonProblem(reading, "Codex", "Planner", "Proposal", expectedAttempts: 1)
            ?? (reading.Outcome == "Proposed" ? null : $"Attempt{reading.Status}{reading.Outcome ?? "None"}")
            ?? ProcessProblem(reading)
            ?? (reading is { MessageType: "Proposal", InReplyToMessageId: null } ? null : "ProposalNotRecorded")
            ?? (reading is { MessageActor: "Codex.Planner", MessageProvenance: "ProviderObserved" }
                ? null
                : "ProposalAuthorMismatch")
            ?? (reading.InputMessageIds.Count == 0 ? null : "UnexpectedInputs")
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
        return CommonProblem(reading, "ClaudeCode", "CriticalReviewer", "CriticalReview", expectedAttempts: 2)
            ?? (expectedType is not null ? null : $"Attempt{reading.Status}{reading.Outcome ?? "None"}")
            ?? ProcessProblem(reading)
            ?? (proposalMessageId is not null && reading.ReviewedProposalMessageId == proposalMessageId
                ? null
                : "ReviewedProposalMismatch")
            ?? (reading.MessageType == expectedType ? null : "ReplyNotRecorded")
            ?? (reading.InReplyToMessageId == proposalMessageId ? null : "ReplyLineageMismatch")
            ?? (reading is { MessageActor: "ClaudeCode.CriticalReviewer", MessageProvenance: "ProviderObserved" }
                ? null
                : "ReplyAuthorMismatch")
            ?? (reading.InputMessageIds.SequenceEqual([proposalMessageId!.Value]) ? null : "InputSetMismatch")
            ?? SealedProblem(reading)
            ?? (reading.ManifestProposalMessageId == proposalMessageId ? null : "SealedProposalMismatch");
    }

    private static string? CommonProblem(
        StageReading reading,
        string provider,
        string role,
        string contract,
        int expectedAttempts)
    {
        if (reading.Provider != provider || reading.Role != role || reading.Contract != contract)
        {
            return "WrongRoleOrProvider";
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

        if (!reading.StatusRouteAgrees)
        {
            return "StatusRouteDisagrees";
        }

        var onlyItsOwnMessage = reading.MessageCount == 1
            && reading.MessageId is not null
            && reading.MessageAttemptId == reading.AttemptId;
        return onlyItsOwnMessage ? null : "MessageNotRecorded";
    }

    private static string? SealedProblem(StageReading reading)
    {
        if (!reading.EvidenceRouteAgrees)
        {
            return "EvidenceRouteDisagrees";
        }

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
