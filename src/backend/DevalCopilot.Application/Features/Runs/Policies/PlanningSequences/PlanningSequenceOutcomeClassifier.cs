using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies.PlanningSequences;

/// <summary>
/// Judges one completed step of a planning sequence from durable identity alone (ADR-0055). It reuses <see cref="PlanningLineage"/> and
/// the existing role-owned eligibility rules and validates the complete predecessor outcome, output cardinality, provenance, author,
/// replies, pinned source and the whole ordered input membership before naming the next identity. It never selects a "latest" proposal,
/// a subset of challenges, an event's attempt attribution or a loaded window, and it never changes how a result is recorded.
/// </summary>
internal static class PlanningSequenceOutcomeClassifier
{
    private const int MaximumChallenges = 5;

    public static PlanningSequenceVerdict Classify(
        ImplementerExecutionReportEligibility.Snapshot snapshot,
        PlanningSequence sequence,
        PlanningSequenceStep step,
        Attempt attempt)
    {
        if (!IsExpectedCompletedAttempt(sequence, step, attempt))
        {
            return PlanningSequenceVerdict.Invalid();
        }

        return step.Ordinal switch
        {
            0 => ClassifyPlanner(snapshot, sequence, attempt),
            1 or 3 => ClassifyReview(snapshot, sequence, step, attempt),
            _ => ClassifyResolution(snapshot, sequence, step, attempt),
        };
    }

    /// <summary>The attempt is the step's own claimed attempt: coherent, of the step's fixed role, provider and contract, Completed,
    /// of this run and bound to exactly the pinned workspace, checkpoint and fingerprint.</summary>
    private static bool IsExpectedCompletedAttempt(PlanningSequence sequence, PlanningSequenceStep step, Attempt attempt) =>
        step.AttemptId == attempt.Id
        && attempt.RunId == sequence.RunId
        && attempt.Status == AttemptStatus.Completed
        && attempt.AgentRole == PlanningSequencePolicy.RoleOf(step.Ordinal)
        && attempt.AgentProvider == PlanningSequencePolicy.ProviderOf(step.Ordinal)
        && attempt.AgentResponseContract == PlanningSequencePolicy.ContractOf(step.Ordinal)
        && AgentAttemptIdentity.IsCoherent(attempt)
        && attempt.AgentGitWorkspaceId == sequence.WorkspaceId
        && attempt.AgentGitCheckpointId == sequence.CheckpointId
        && string.Equals(attempt.AgentCheckpointFingerprintSha256, sequence.FingerprintSha256, StringComparison.Ordinal);

    private static PlanningSequenceVerdict ClassifyPlanner(
        ImplementerExecutionReportEligibility.Snapshot snapshot, PlanningSequence sequence, Attempt attempt)
    {
        if (attempt.AgentOutcome != AgentOutcome.Proposed || snapshot.InputsFor(attempt.Id).Count != 0)
        {
            return PlanningSequenceVerdict.Invalid();
        }

        var outputs = OutputsOf(snapshot, attempt);
        if (outputs.Length != 1
            || outputs[0].Type != CollaborationMessageType.Proposal
            || outputs[0].Provenance != CollaborationMessageProvenance.ProviderObserved)
        {
            return PlanningSequenceVerdict.Invalid();
        }

        var evaluation = PlanningLineage.Evaluate(
            snapshot, sequence.RunId, sequence.WorkspaceId, sequence.CheckpointId, sequence.FingerprintSha256, outputs[0].Id);
        return evaluation.Node is { Depth: 0 } node && node.Owner.Id == attempt.Id
            ? new PlanningSequenceVerdict(
                PlanningSequenceVerdictKind.Continue, PredecessorAttemptId: attempt.Id, InputProposalMessageId: node.Proposal.Id)
            : PlanningSequenceVerdict.Invalid();
    }

    private static PlanningSequenceVerdict ClassifyReview(
        ImplementerExecutionReportEligibility.Snapshot snapshot, PlanningSequence sequence, PlanningSequenceStep step, Attempt attempt)
    {
        if (step.InputProposalMessageId is not { } proposalId
            || step.PredecessorAttemptId is not { } predecessorId
            || !IsSoleInput(snapshot, attempt, proposalId)
            || !snapshot.MessagesById.TryGetValue(proposalId, out var proposal)
            || proposal.RunId != sequence.RunId
            || proposal.Type != CollaborationMessageType.Proposal
            || proposal.Provenance != CollaborationMessageProvenance.ProviderObserved
            || proposal.AttemptId != predecessorId)
        {
            return PlanningSequenceVerdict.Invalid();
        }

        // The reviewed Proposal must itself be a valid lineage Proposal of the pinned source at the depth this step reviews.
        var evaluation = PlanningLineage.Evaluate(
            snapshot, sequence.RunId, sequence.WorkspaceId, sequence.CheckpointId, sequence.FingerprintSha256, proposalId);
        if (evaluation.Node is not { } node
            || node.Owner.Id != predecessorId
            || node.Depth != (step.Ordinal == 1 ? 0 : 1))
        {
            return PlanningSequenceVerdict.Invalid();
        }

        if (attempt.AgentOutcome == AgentOutcome.Accepted)
        {
            var acceptance = PlanningLineage.FindExactAcceptance(
                snapshot, attempt, sequence.RunId, proposalId, sequence.WorkspaceId, sequence.CheckpointId, sequence.FingerprintSha256);
            return acceptance is null
                ? PlanningSequenceVerdict.Invalid()
                : new PlanningSequenceVerdict(
                    PlanningSequenceVerdictKind.Accepted, FinalProposalMessageId: proposalId, ClosingMessageId: acceptance.Id);
        }

        if (attempt.AgentOutcome != AgentOutcome.Challenged)
        {
            return PlanningSequenceVerdict.Invalid();
        }

        var challenges = OutputsOf(snapshot, attempt);
        var reviewer = ParticipantIdentity.ForAgent(AgentRole.CriticalReviewer, AgentProvider.ClaudeCode);
        return challenges.Length is >= 1 and <= MaximumChallenges
            && challenges.All(challenge => challenge.Type == CollaborationMessageType.Challenge
                && challenge.Provenance == CollaborationMessageProvenance.ProviderObserved
                && challenge.InReplyToMessageId == proposalId
                && challenge.Sequence > proposal.Sequence
                && challenge.Actor == reviewer)
                ? new PlanningSequenceVerdict(
                    PlanningSequenceVerdictKind.Continue, PredecessorAttemptId: attempt.Id, InputProposalMessageId: proposalId)
                : PlanningSequenceVerdict.Invalid();
    }

    private static PlanningSequenceVerdict ClassifyResolution(
        ImplementerExecutionReportEligibility.Snapshot snapshot, PlanningSequence sequence, PlanningSequenceStep step, Attempt attempt)
    {
        if (attempt.AgentOutcome != AgentOutcome.Resolved
            || step.InputProposalMessageId is not { } reviewedProposalId
            || step.PredecessorAttemptId is not { } reviewId
            || !snapshot.AttemptsById.TryGetValue(reviewId, out var review)
            || review.Status != AttemptStatus.Completed
            || review.AgentOutcome != AgentOutcome.Challenged)
        {
            return PlanningSequenceVerdict.Invalid();
        }

        // The whole ordered input set: the reviewed Proposal, then every Challenge of exactly that review in recorded order.
        var expectedChallenges = OutputsOf(snapshot, review);
        var inputs = snapshot.InputsFor(attempt.Id);
        if (expectedChallenges.Length == 0
            || inputs.Count != expectedChallenges.Length + 1
            || inputs[0].CollaborationMessageId != reviewedProposalId
            || !inputs.Skip(1).Select(input => input.CollaborationMessageId).SequenceEqual(expectedChallenges.Select(challenge => challenge.Id)))
        {
            return PlanningSequenceVerdict.Invalid();
        }

        var revisions = snapshot.Messages
            .Where(message => message.AttemptId == attempt.Id && message.Type == CollaborationMessageType.Proposal)
            .ToArray();
        if (revisions.Length != 1 || revisions[0].Provenance != CollaborationMessageProvenance.ProviderObserved)
        {
            return PlanningSequenceVerdict.Invalid();
        }

        var evaluation = PlanningLineage.Evaluate(
            snapshot, sequence.RunId, sequence.WorkspaceId, sequence.CheckpointId, sequence.FingerprintSha256, revisions[0].Id);
        var expectedDepth = step.Ordinal == 2 ? 1 : 2;
        if (evaluation.Node is not { } node
            || node.Depth != expectedDepth
            || node.Owner.Id != attempt.Id
            || node.ChallengedReview?.Id != reviewId
            || node.Parent?.Proposal.Id != reviewedProposalId)
        {
            return PlanningSequenceVerdict.Invalid();
        }

        if (expectedDepth == 1)
        {
            return new PlanningSequenceVerdict(
                PlanningSequenceVerdictKind.Continue, PredecessorAttemptId: attempt.Id, InputProposalMessageId: node.Proposal.Id);
        }

        var escalation = FindEscalation(snapshot, node);
        return escalation is null
            ? PlanningSequenceVerdict.Invalid()
            : new PlanningSequenceVerdict(
                PlanningSequenceVerdictKind.Escalated, FinalProposalMessageId: node.Proposal.Id, ClosingMessageId: escalation.Id);
    }

    /// <summary>The one host-constructed Escalation that replies to the depth-two Proposal with the canonical content recomputed from
    /// the validated root, first revision, second revision and complete second-round Challenge identities.</summary>
    private static CollaborationMessage? FindEscalation(ImplementerExecutionReportEligibility.Snapshot snapshot, PlanningLineage.Node node)
    {
        var escalations = snapshot.Messages
            .Where(message => message.Type == CollaborationMessageType.Escalation
                && message.InReplyToMessageId == node.Proposal.Id
                && message.Provenance == CollaborationMessageProvenance.HostConstructed
                && message.AttemptId is null)
            .ToArray();
        return escalations.Length == 1
            && node.Parent is { } first
            && escalations[0].Actor == ParticipantIdentity.ForOrchestrator()
            && string.Equals(escalations[0].Summary, PlanningEscalation.Summary, StringComparison.Ordinal)
            && PlanningEscalation.IsCanonicalContent(
                escalations[0].StructuredContentJson,
                node.Root.Proposal.Id,
                first.Proposal.Id,
                node.Proposal.Id,
                node.Challenges.Select(challenge => challenge.Id).ToArray())
                ? escalations[0]
                : null;
    }

    private static bool IsSoleInput(ImplementerExecutionReportEligibility.Snapshot snapshot, Attempt attempt, Guid proposalMessageId)
    {
        var inputs = snapshot.InputsFor(attempt.Id);
        return inputs.Count == 1 && inputs[0].Sequence == 0 && inputs[0].CollaborationMessageId == proposalMessageId;
    }

    private static CollaborationMessage[] OutputsOf(ImplementerExecutionReportEligibility.Snapshot snapshot, Attempt attempt) =>
        snapshot.Messages.Where(message => message.AttemptId == attempt.Id).OrderBy(message => message.Sequence).ToArray();
}
