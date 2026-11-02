namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The closed Domain rule for the one manual format repair of the three read-only collaboration
/// stages beyond the Planner: CriticalReviewer (<see cref="AgentResponseContract.CriticalReview"/>),
/// Resolver (<see cref="AgentResponseContract.ChallengeResolution"/>), and CodeReviewer
/// (<see cref="AgentResponseContract.ImplementationReview"/>). It pins, per response contract, the
/// exact provider, role, expected message type, permission profile, and current adapter
/// contract a source attempt must carry (a CriticalReviewer source may also carry the historical
/// v1 contract), and evaluates a persisted attempt against them. It reads
/// persisted values only, so an incoherent row is never eligible. Cross-row facts (the run's latest
/// Agent attempt, no earlier repair, no semantic messages, exact current inputs) belong to the
/// Application handlers. The Planner has its own, unchanged rule
/// (<see cref="Attempt.IsEligiblePlanningRepairSource"/>).
/// </summary>
public static class ReadOnlyFormatRepairPolicy
{
    /// <summary>ADR-0072: the current CriticalReviewer adapter contract, which reads only the schema-validated
    /// <c>structured_output</c> of the documented result envelope. Every new critical review, including a repair, uses it.</summary>
    public const string CriticalReviewAdapterContractVersion = "claude-critical-review-v2";

    /// <summary>The historical CriticalReviewer contract that read the narrative result. Its attempts stay as recorded and are
    /// never redispatched; a failed one may still be the source of one repair, which runs under the current contract.</summary>
    public const string HistoricalCriticalReviewAdapterContractVersionV1 = "claude-critical-review-v1";

    public const string ChallengeResolutionAdapterContractVersion = "codex-challenge-resolution-v1";
    public const string ImplementationReviewAdapterContractVersion = "codex-implementation-review-v1";

    /// <summary>Whether a manual format repair exists for the response contract.</summary>
    public static bool Supports(AgentResponseContract responseContract) => responseContract is
        AgentResponseContract.CriticalReview
        or AgentResponseContract.ChallengeResolution
        or AgentResponseContract.ImplementationReview;

    /// <summary>The exact provider/role/expected-message/adapter tuple a repair source of the
    /// contract must have, or <see langword="null"/> for an unsupported contract.</summary>
    public static (AgentProvider Provider, AgentRole Role, CollaborationMessageType ExpectedMessageType, string AdapterContractVersion)?
        ExpectedTuple(AgentResponseContract responseContract) => responseContract switch
        {
            AgentResponseContract.CriticalReview =>
                (AgentProvider.ClaudeCode, AgentRole.CriticalReviewer, CollaborationMessageType.Proposal, CriticalReviewAdapterContractVersion),
            AgentResponseContract.ChallengeResolution =>
                (AgentProvider.Codex, AgentRole.Resolver, CollaborationMessageType.Proposal, ChallengeResolutionAdapterContractVersion),
            AgentResponseContract.ImplementationReview =>
                (AgentProvider.Codex, AgentRole.CodeReviewer, CollaborationMessageType.ReviewFinding, ImplementationReviewAdapterContractVersion),
            _ => null,
        };

    /// <summary>Whether <paramref name="source"/> may be the source of the one manual format repair
    /// for <paramref name="responseContract"/>: a <see cref="AttemptStatus.Failed"/>, dispatched and
    /// concluded Agent attempt whose recorded outcome is exactly
    /// <see cref="AgentOutcome.InvalidStructuredOutput"/>, whose host-measured process evidence
    /// proves a clean exit, whose provider, role, response contract, expected message type,
    /// protocol version, permission profile, and adapter contract are the path's exact current
    /// tuple, whose assignment snapshot is well formed, and which is not itself a repair.</summary>
    public static bool IsEligibleSource(Attempt source, AgentResponseContract responseContract)
    {
        return HasRepairableSourceTuple(source, responseContract)
            && source.Status == AttemptStatus.Failed
            && source.AgentOutcome == AgentOutcome.InvalidStructuredOutput
            && source.AgentDispatchedAtUtc.HasValue
            && source.CompletedAtUtc.HasValue
            && source.AgentGitWorkspaceId.HasValue
            && source.AgentGitCheckpointId.HasValue
            && source.AgentCheckpointFingerprintSha256 is not null
            && source.AgentRepairSourceAttemptId is null
            && source.GetAgentProcessExecutionEvidence() is { IsCleanExit: true }
            && source.GetAssignmentSnapshot() is not null;
    }

    /// <summary>Whether the attempt carries the response contract's exact provider, role, expected
    /// message type, protocol version, read-only permission profile, and current adapter
    /// contract — the coherent tuple of an attempt of this path, whatever its state.</summary>
    public static bool HasExactTuple(Attempt attempt, AgentResponseContract responseContract) =>
        ExpectedTuple(responseContract) is { } expected
        && HasTupleWithAdapter(attempt, responseContract, attempt.AgentAdapterContractVersion == expected.AdapterContractVersion);

    /// <summary>The source side of a repair: the same exact tuple, except that a CriticalReviewer source may also carry the
    /// historical v1 contract (ADR-0072). The source itself is never changed or redispatched; only the new repair attempt runs,
    /// under the current contract.</summary>
    public static bool HasRepairableSourceTuple(Attempt attempt, AgentResponseContract responseContract) =>
        ExpectedTuple(responseContract) is { } expected
        && HasTupleWithAdapter(
            attempt,
            responseContract,
            attempt.AgentAdapterContractVersion == expected.AdapterContractVersion
            || (responseContract == AgentResponseContract.CriticalReview
                && attempt.AgentAdapterContractVersion == HistoricalCriticalReviewAdapterContractVersionV1));

    /// <summary>Whether <paramref name="version"/> is the current or the historical CriticalReviewer contract.</summary>
    public static bool IsKnownCriticalReviewVersion(string? version) =>
        version is CriticalReviewAdapterContractVersion or HistoricalCriticalReviewAdapterContractVersionV1;

    private static bool HasTupleWithAdapter(Attempt attempt, AgentResponseContract responseContract, bool adapterMatches)
    {
        if (ExpectedTuple(responseContract) is not { } expected)
        {
            return false;
        }

        return attempt.Kind == AttemptKind.Agent
            && attempt.AgentProvider == expected.Provider
            && attempt.AgentRole == expected.Role
            && attempt.AgentResponseContract == responseContract
            && attempt.AgentExpectedMessageType == expected.ExpectedMessageType
            && attempt.AgentProtocolVersion == CollaborationMessage.ProtocolVersionOne
            && attempt.AgentPermissionProfile == AgentPermissionProfile.ReadOnly
            && adapterMatches;
    }
}
