using Devalente.Shared.Results;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs;

/// <summary>
/// The one Application-owned rule for the Proposal → Challenge → Decision → revised Proposal
/// lineage, derived only from durable identity: same-run, provider-observed messages, their exact
/// owning Agent attempts, roles, response contracts, ordered attempt inputs, reply links, and
/// the exact workspace and checkpoint. Nothing here compares natural-language text or guesses that
/// two challenges are the same issue.
///
/// <para>
/// A valid <b>root</b> (depth 0) is a completed Planner Proposal. A Resolver's revised Proposal
/// whose ordered inputs are a valid parent Proposal plus that parent's complete Challenged review
/// is one level deeper. Depth one may receive one optional critical review and, if challenged, one
/// more resolution (depth two). Depth two is the last level: it is neither reviewable nor
/// implementable, so the lineage-wide cap is conservative relative to the roadmap's per-issue
/// wording. Evaluation is bounded and cycle-safe, and fails closed on any missing, foreign, stale,
/// duplicated, or incoherent evidence without echoing stored text.
/// </para>
/// </summary>
internal static class PlanningLineage
{
    public const int MaximumDepth = 2;
    public const int MaximumReviewableDepth = 1;
    public const int MaximumImplementableDepth = 1;

    private const int MaximumChallengesPerReview = 5;

    internal enum FailureKind
    {
        NotFound,
        NotProposal,
        OwnerInvalid,
        CheckpointStale,
        LineageInvalid,
    }

    /// <summary>One validated Proposal. <see cref="Owner"/> is the Planner attempt for a root and the
    /// Resolver attempt for a revision; the review, challenge, and decision facts are empty for a
    /// root.</summary>
    internal sealed record Node(
        CollaborationMessage Proposal,
        Attempt Owner,
        int Depth,
        Node? Parent,
        Attempt? ChallengedReview,
        IReadOnlyList<CollaborationMessage> Challenges,
        IReadOnlyList<CollaborationMessage> Decisions)
    {
        public Node Root => Parent is null ? this : Parent.Root;
    }

    internal sealed record Evaluation(Node? Node, FailureKind? Failure)
    {
        public static Evaluation Success(Node node) => new(node, null);

        public static Evaluation Fail(FailureKind kind) => new(null, kind);
    }

    /// <summary>
    /// Evaluates <paramref name="proposalMessageId"/> as a lineage Proposal bound to exactly the
    /// given workspace and checkpoint (and fingerprint when supplied). Depth is reported, never
    /// judged: each caller decides whether that depth is reviewable, resolvable, or implementable.
    /// </summary>
    public static Evaluation Evaluate(
        ImplementerExecutionReportEligibility.Snapshot snapshot,
        Guid runId,
        Guid workspaceId,
        Guid checkpointId,
        string? fingerprintSha256,
        Guid proposalMessageId) =>
        EvaluateCore(snapshot, runId, workspaceId, checkpointId, fingerprintSha256, proposalMessageId, [], isTopLevel: true);

    private static Evaluation EvaluateCore(
        ImplementerExecutionReportEligibility.Snapshot snapshot,
        Guid runId,
        Guid workspaceId,
        Guid checkpointId,
        string? fingerprintSha256,
        Guid proposalMessageId,
        HashSet<Guid> visited,
        bool isTopLevel)
    {
        if (!visited.Add(proposalMessageId))
        {
            return Evaluation.Fail(FailureKind.LineageInvalid);
        }

        if (!snapshot.MessagesById.TryGetValue(proposalMessageId, out var proposal) || proposal.RunId != runId)
        {
            return Evaluation.Fail(isTopLevel ? FailureKind.NotFound : FailureKind.LineageInvalid);
        }

        if (proposal.Type != CollaborationMessageType.Proposal
            || proposal.Provenance != CollaborationMessageProvenance.ProviderObserved
            || proposal.AttemptId is null)
        {
            return Evaluation.Fail(isTopLevel ? FailureKind.NotProposal : FailureKind.LineageInvalid);
        }

        var plannerOwner = ResolveOwner(
            snapshot, proposal, runId, AgentRole.Planner);
        if (plannerOwner is not null)
        {
            return EvaluateRoot(plannerOwner, proposal, workspaceId, checkpointId, fingerprintSha256);
        }

        var resolverOwner = ResolveOwner(
            snapshot, proposal, runId, AgentRole.Resolver);
        if (resolverOwner is null)
        {
            return Evaluation.Fail(FailureKind.OwnerInvalid);
        }

        return EvaluateRevision(
            snapshot, runId, resolverOwner, proposal, workspaceId, checkpointId, fingerprintSha256, visited);
    }

    private static Evaluation EvaluateRoot(
        Attempt owner,
        CollaborationMessage proposal,
        Guid workspaceId,
        Guid checkpointId,
        string? fingerprintSha256)
    {
        if (owner.Status != AttemptStatus.Completed
            || owner.AgentOutcome != AgentOutcome.Proposed
            || owner.AgentResponseContract != AgentResponseContract.Proposal)
        {
            return Evaluation.Fail(FailureKind.OwnerInvalid);
        }

        if (!IsBoundTo(owner, workspaceId, checkpointId, fingerprintSha256))
        {
            return Evaluation.Fail(FailureKind.CheckpointStale);
        }

        return Evaluation.Success(new Node(proposal, owner, 0, null, null, [], []));
    }

    private static Evaluation EvaluateRevision(
        ImplementerExecutionReportEligibility.Snapshot snapshot,
        Guid runId,
        Attempt resolver,
        CollaborationMessage revised,
        Guid workspaceId,
        Guid checkpointId,
        string? fingerprintSha256,
        HashSet<Guid> visited)
    {
        if (resolver.Status != AttemptStatus.Completed
            || resolver.AgentOutcome != AgentOutcome.Resolved
            || resolver.AgentResponseContract != AgentResponseContract.ChallengeResolution)
        {
            return Evaluation.Fail(FailureKind.OwnerInvalid);
        }

        if (!IsBoundTo(resolver, workspaceId, checkpointId, fingerprintSha256))
        {
            return Evaluation.Fail(FailureKind.CheckpointStale);
        }

        var inputs = snapshot.InputsFor(resolver.Id);
        if (inputs.Count < 2
            || inputs.Count > MaximumChallengesPerReview + 1
            || !ImplementerExecutionReportEligibility.HasExactContiguousInputSequence(inputs)
            || revised.InReplyToMessageId != inputs[0].CollaborationMessageId)
        {
            return Evaluation.Fail(FailureKind.LineageInvalid);
        }

        var parentEvaluation = EvaluateCore(
            snapshot, runId, workspaceId, checkpointId, fingerprintSha256, inputs[0].CollaborationMessageId, visited,
            isTopLevel: false);
        if (parentEvaluation.Node is not { } parent)
        {
            return Evaluation.Fail(parentEvaluation.Failure ?? FailureKind.LineageInvalid);
        }

        // A depth-two Proposal is the last valid level: nothing may be revised from it.
        var depth = parent.Depth + 1;
        if (depth > MaximumDepth || revised.Sequence <= parent.Proposal.Sequence)
        {
            return Evaluation.Fail(FailureKind.LineageInvalid);
        }

        var challengeIds = inputs.Skip(1).Select(input => input.CollaborationMessageId).ToArray();
        if (challengeIds.Distinct().Count() != challengeIds.Length)
        {
            return Evaluation.Fail(FailureKind.LineageInvalid);
        }

        var reviewMatches = snapshot.AttemptsById.Values
            .Where(candidate => candidate.RunId == runId
                && candidate.Kind == AttemptKind.Agent
                && candidate.AgentRole == AgentRole.CriticalReviewer
                && candidate.AgentResponseContract == AgentResponseContract.CriticalReview
                && candidate.Status == AttemptStatus.Completed
                && candidate.AgentOutcome == AgentOutcome.Challenged
                && AgentAttemptIdentity.IsCoherent(candidate)
                && IsSoleInput(snapshot, candidate, parent.Proposal.Id))
            .ToArray();
        if (reviewMatches.Length != 1)
        {
            return Evaluation.Fail(FailureKind.LineageInvalid);
        }

        var review = reviewMatches[0];
        if (!IsBoundTo(review, workspaceId, checkpointId, fingerprintSha256))
        {
            return Evaluation.Fail(FailureKind.CheckpointStale);
        }

        var challenges = snapshot.Messages
            .Where(message => message.AttemptId == review.Id)
            .OrderBy(message => message.Sequence)
            .ToArray();
        if (challenges.Length != challengeIds.Length
            || !challenges.Select(challenge => challenge.Id).SequenceEqual(challengeIds)
            || challenges.Any(challenge => challenge.Type != CollaborationMessageType.Challenge
                || challenge.InReplyToMessageId != parent.Proposal.Id
                || challenge.Sequence <= parent.Proposal.Sequence
                || ResolveOwner(
                    snapshot, challenge, runId, AgentRole.CriticalReviewer)?.Id != review.Id))
        {
            return Evaluation.Fail(FailureKind.LineageInvalid);
        }

        var decisions = snapshot.Messages
            .Where(message => message.AttemptId == resolver.Id && message.Type == CollaborationMessageType.Decision)
            .OrderBy(message => message.Sequence)
            .ToArray();
        if (decisions.Length != challenges.Length
            || !decisions.Select(decision => decision.InReplyToMessageId).SequenceEqual(challengeIds.Select(id => (Guid?)id))
            || decisions.Zip(challenges).Any(pair => pair.First.Sequence <= pair.Second.Sequence
                || ResolveOwner(
                    snapshot, pair.First, runId, AgentRole.Resolver)?.Id != resolver.Id))
        {
            return Evaluation.Fail(FailureKind.LineageInvalid);
        }

        var ownProposals = snapshot.Messages
            .Where(message => message.AttemptId == resolver.Id
                && message.Type == CollaborationMessageType.Proposal
                && message.Provenance == CollaborationMessageProvenance.ProviderObserved)
            .ToArray();
        if (ownProposals.Length != 1 || ownProposals[0].Id != revised.Id || revised.Sequence <= decisions[^1].Sequence)
        {
            return Evaluation.Fail(FailureKind.LineageInvalid);
        }

        return Evaluation.Success(new Node(revised, resolver, depth, parent, review, challenges, decisions));
    }

    /// <summary>The stable, non-echoing refusal for a failed lineage evaluation at a review or
    /// resolution boundary. Messages are fixed text; no stored evidence is ever repeated.</summary>
    public static Error ToClaimError(FailureKind failure) => failure switch
    {
        FailureKind.NotFound => Error.NotFound(
            "agent_attempts.proposal_not_found", "The requested proposal was not found for this run."),
        FailureKind.NotProposal => Error.Conflict(
            "agent_attempts.not_provider_observed_planner_proposal",
            "Only a provider-observed Planner proposal or Resolver revision is part of a reviewable proposal lineage."),
        FailureKind.OwnerInvalid => Error.Conflict(
            "agent_attempts.proposal_attempt_not_valid",
            "The proposal's owning attempt did not complete as a valid proposal."),
        FailureKind.CheckpointStale => Error.Conflict(
            "agent_attempts.proposal_checkpoint_stale",
            "The proposal lineage was made against a different checkpoint than the one currently current for this workspace."),
        _ => Error.Conflict(
            "agent_attempts.proposal_lineage_not_valid",
            "The proposal's lineage is not a valid, complete, provider-observed chain."),
    };

    public const string ExhaustedCode = "agent_attempts.proposal_lineage_exhausted";

    /// <summary>
    /// The owning attempt of a provider-observed lineage message, or <see langword="null"/>. Beyond the
    /// role-first message checks, the attempt's persisted identity must be one DevalCopilot actually
    /// launches (<see cref="AgentAttemptIdentity.IsCoherent"/>: defined status, role, provider, and
    /// response contract, a supported role/provider pair, a contract that belongs to the role, a
    /// well-formed assignment), so a forged pair with a matching message actor never authorizes the
    /// next step. Role still decides which lineage role a message may hold.
    /// </summary>
    private static Attempt? ResolveOwner(
        ImplementerExecutionReportEligibility.Snapshot snapshot, CollaborationMessage message, Guid runId, AgentRole role)
    {
        if (message.RunId != runId
            || message.Provenance != CollaborationMessageProvenance.ProviderObserved
            || message.AttemptId is not { } attemptId
            || !snapshot.AttemptsById.TryGetValue(attemptId, out var attempt)
            || attempt.RunId != runId
            || !AgentAttemptIdentity.IsCoherent(attempt)
            || attempt.AgentRole != role
            || attempt.AgentProvider is not { } provider
            || message.Actor != ParticipantIdentity.ForAgent(role, provider))
        {
            return null;
        }

        return attempt;
    }

    /// <summary>
    /// Loads the run's attempts and messages for lineage evaluation, or <see langword="null"/> when a
    /// persisted row cannot be materialized (typically an unparseable stored enum string, though the
    /// exception type alone cannot prove that). Any single unreadable row of the run makes the whole
    /// snapshot unreadable, so an unrelated corrupt row fails the evaluation closed rather than being
    /// skipped. The exception and stored value are never surfaced. The guard catches
    /// <see cref="InvalidOperationException"/>, which cannot prove an enum-conversion cause and could have
    /// another origin; <see cref="System.Data.Common.DbException"/> and cancellation exceptions are not caught
    /// by it.
    /// </summary>
    public static async Task<ImplementerExecutionReportEligibility.Snapshot?> TryLoadSnapshotAsync(
        Data.IDevalCopilotDbContext dbContext, Guid runId, CancellationToken cancellationToken)
    {
        try
        {
            return await ImplementerExecutionReportEligibility.LoadSnapshotAsync(dbContext, runId, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public static Error UnreadableEvidenceError() => ToClaimError(FailureKind.LineageInvalid);

    public static Error ExhaustedError() => Error.Conflict(
        ExhaustedCode,
        "This proposal lineage has used its second challenge round; it needs a human decision or a new planning request.");

    /// <summary>
    /// Every completed, successful (Accepted or Challenged) critical review whose sole input is
    /// <paramref name="proposalMessageId"/>, ordered by attempt number.
    /// </summary>
    public static IReadOnlyList<Attempt> SuccessfulReviewsOf(
        ImplementerExecutionReportEligibility.Snapshot snapshot, Guid runId, Guid proposalMessageId) =>
        snapshot.AttemptsById.Values
            .Where(candidate => candidate.RunId == runId
                && candidate.Kind == AttemptKind.Agent
                && candidate.AgentRole == AgentRole.CriticalReviewer
                && candidate.Status == AttemptStatus.Completed
                && (candidate.AgentOutcome == AgentOutcome.Accepted || candidate.AgentOutcome == AgentOutcome.Challenged)
                && IsSoleInput(snapshot, candidate, proposalMessageId))
            .OrderBy(candidate => candidate.AttemptNumber)
            .ToArray();

    /// <summary>
    /// The one exact Acceptance of an Accepted critical review of <paramref name="proposalMessageId"/>:
    /// a completed CriticalReview attempt bound to the exact workspace and checkpoint whose single
    /// output is a provider-observed Acceptance replying to that Proposal. <see langword="null"/>
    /// when any part of that evidence is missing or incoherent.
    /// </summary>
    public static CollaborationMessage? FindExactAcceptance(
        ImplementerExecutionReportEligibility.Snapshot snapshot,
        Attempt review,
        Guid runId,
        Guid proposalMessageId,
        Guid workspaceId,
        Guid checkpointId,
        string? fingerprintSha256)
    {
        if (review.Status != AttemptStatus.Completed
            || review.AgentOutcome != AgentOutcome.Accepted
            || review.AgentRole != AgentRole.CriticalReviewer
            || review.AgentResponseContract != AgentResponseContract.CriticalReview
            || !IsBoundTo(review, workspaceId, checkpointId, fingerprintSha256)
            || !IsSoleInput(snapshot, review, proposalMessageId))
        {
            return null;
        }

        var outputs = snapshot.Messages.Where(message => message.AttemptId == review.Id).ToArray();
        if (outputs.Length != 1)
        {
            return null;
        }

        var acceptance = outputs[0];
        var proposal = snapshot.MessagesById.GetValueOrDefault(proposalMessageId);
        return proposal is not null
            && acceptance.Type == CollaborationMessageType.Acceptance
            && acceptance.InReplyToMessageId == proposalMessageId
            && acceptance.Sequence > proposal.Sequence
            && ResolveOwner(
                snapshot, acceptance, runId, AgentRole.CriticalReviewer)?.Id == review.Id
                ? acceptance
                : null;
    }

    /// <summary>
    /// Whether <paramref name="owner"/> authored exactly one provider-observed Proposal. A lineage
    /// that will be implemented must be free of a duplicated root Proposal; review and resolution
    /// claims do not need that stricter proof and never required it.
    /// </summary>
    public static bool OwnsExactlyOneProposal(ImplementerExecutionReportEligibility.Snapshot snapshot, Attempt owner) =>
        snapshot.Messages.Count(message => message.AttemptId == owner.Id
            && message.Type == CollaborationMessageType.Proposal
            && message.Provenance == CollaborationMessageProvenance.ProviderObserved) == 1;

    private static bool IsSoleInput(
        ImplementerExecutionReportEligibility.Snapshot snapshot, Attempt attempt, Guid proposalMessageId)
    {
        var inputs = snapshot.InputsFor(attempt.Id);
        return inputs.Count == 1 && inputs[0].Sequence == 0 && inputs[0].CollaborationMessageId == proposalMessageId;
    }

    private static bool IsBoundTo(Attempt attempt, Guid workspaceId, Guid checkpointId, string? fingerprintSha256) =>
        attempt.AgentGitWorkspaceId == workspaceId
        && attempt.AgentGitCheckpointId == checkpointId
        && (fingerprintSha256 is null
            || string.Equals(attempt.AgentCheckpointFingerprintSha256, fingerprintSha256, StringComparison.Ordinal));
}
