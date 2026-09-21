using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs;

/// <summary>
/// Resolves the authoritative Implementer execution-report chain used by review and correction
/// workflows. The semantic authority is the Implementer role plus the response contract and
/// outcome pair; the provider is checked only as persisted provenance integrity.
/// </summary>
internal static class ImplementerExecutionReportEligibility
{
    internal sealed record Result(
        Attempt OwnerAttempt,
        CollaborationMessage ExecutionReport,
        CollaborationMessage OriginalProposal,
        CollaborationMessage? PreviousExecutionReport,
        IReadOnlyList<CollaborationMessage> OrderedFindings,
        IReadOnlyList<CollaborationMessage> OrderedRevisionResponses);

    internal sealed class Snapshot(
        IReadOnlyList<Attempt> attempts,
        IReadOnlyList<CollaborationMessage> messages,
        IReadOnlyList<AttemptInputMessage> inputs,
        IReadOnlyList<GitCheckpoint> checkpoints)
    {
        public IReadOnlyDictionary<Guid, Attempt> AttemptsById { get; } = attempts.ToDictionary(attempt => attempt.Id);

        public IReadOnlyDictionary<Guid, CollaborationMessage> MessagesById { get; } =
            messages.ToDictionary(message => message.Id);

        public IReadOnlyList<CollaborationMessage> Messages { get; } = messages;

        public IReadOnlyDictionary<Guid, IReadOnlyList<AttemptInputMessage>> InputsByAttemptId { get; } =
            inputs.GroupBy(input => input.AttemptId)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<AttemptInputMessage>)group.OrderBy(input => input.Sequence).ToArray());

        public IReadOnlyDictionary<Guid, Guid> CurrentCheckpointByWorkspaceId { get; } =
            checkpoints.GroupBy(checkpoint => checkpoint.WorkspaceId)
                .ToDictionary(group => group.Key, group => group.OrderByDescending(checkpoint => checkpoint.CheckpointNumber).First().Id);

        public IReadOnlyList<AttemptInputMessage> InputsFor(Guid attemptId) =>
            InputsByAttemptId.GetValueOrDefault(attemptId) ?? [];

        public Guid? CurrentCheckpointFor(Guid workspaceId) =>
            CurrentCheckpointByWorkspaceId.GetValueOrDefault(workspaceId);
    }

    internal static async Task<Snapshot> LoadSnapshotAsync(
        IDevalCopilotDbContext dbContext, Guid runId, CancellationToken cancellationToken)
    {
        var attempts = await dbContext.Attempts.AsNoTracking()
            .Where(attempt => attempt.RunId == runId)
            .ToListAsync(cancellationToken);
        var messages = await dbContext.CollaborationMessages.AsNoTracking()
            .Where(message => message.RunId == runId)
            .ToListAsync(cancellationToken);
        var attemptIds = attempts.Select(attempt => attempt.Id).ToArray();
        var inputs = await dbContext.AttemptInputMessages.AsNoTracking()
            .Where(input => attemptIds.Contains(input.AttemptId))
            .ToListAsync(cancellationToken);
        var workspaceIds = attempts
            .Where(attempt => attempt.AgentGitWorkspaceId is not null)
            .Select(attempt => attempt.AgentGitWorkspaceId!.Value)
            .Distinct()
            .ToArray();
        var checkpoints = await dbContext.GitCheckpoints.AsNoTracking()
            .Where(checkpoint => workspaceIds.Contains(checkpoint.WorkspaceId))
            .ToListAsync(cancellationToken);
        return new Snapshot(attempts, messages, inputs, checkpoints);
    }

    internal static Result? Resolve(
        Snapshot snapshot,
        CollaborationMessage executionReport,
        Guid runId,
        Guid workspaceId,
        Guid currentCheckpointId)
    {
        return ResolveSnapshotCore(snapshot, executionReport, runId, workspaceId, currentCheckpointId, []);
    }

    public static async Task<Result?> ResolveAsync(
        IDevalCopilotDbContext dbContext,
        CollaborationMessage executionReport,
        Guid runId,
        Guid workspaceId,
        Guid currentCheckpointId,
        CancellationToken cancellationToken)
    {
        var snapshot = await LoadSnapshotAsync(dbContext, runId, cancellationToken);
        return Resolve(snapshot, executionReport, runId, workspaceId, currentCheckpointId);
    }

    private static Result? ResolveSnapshotCore(
        Snapshot snapshot,
        CollaborationMessage executionReport,
        Guid runId,
        Guid workspaceId,
        Guid currentCheckpointId,
        HashSet<Guid> visitedReports)
    {
        if (!visitedReports.Add(executionReport.Id)
            || executionReport.RunId != runId
            || executionReport.Type != CollaborationMessageType.ExecutionReport)
        {
            return null;
        }

        var owner = ResolveSnapshotOwningAttempt(snapshot, executionReport, runId, AgentRole.Implementer);
        if (owner is null)
        {
            return null;
        }
        if (owner.Status != AttemptStatus.Completed
            || owner.AgentResultGitCheckpointId != currentCheckpointId
            || owner.AgentGitWorkspaceId != workspaceId
            || owner.AgentResponseContract is not { } ownerContract
            || owner.AgentOutcome is not { } ownerOutcome
            || !IsValidReportPair(ownerContract, ownerOutcome))
        {
            return null;
        }
        if (snapshot.Messages.Count(message => message.AttemptId == owner.Id
                && message.Type == CollaborationMessageType.ExecutionReport
                && message.Provenance == CollaborationMessageProvenance.ProviderObserved) != 1)
        {
            return null;
        }
        if (executionReport.InReplyToMessageId is not { } parentId
            || !snapshot.MessagesById.TryGetValue(parentId, out var parent)
            || parent.RunId != runId
            || parent.Type != CollaborationMessageType.Proposal
            || executionReport.Sequence <= parent.Sequence)
        {
            return null;
        }
        var contract = owner.AgentResponseContract!.Value;
        var outcome = owner.AgentOutcome!.Value;

        var orderedInputs = snapshot.InputsFor(owner.Id);
        var inputMessages = orderedInputs
            .Select(input => snapshot.MessagesById.GetValueOrDefault(input.CollaborationMessageId))
            .ToArray();
        if (inputMessages.Any(message => message is null))
        {
            return null;
        }

        if (contract == AgentResponseContract.ImplementationReport)
        {
            if (outcome != AgentOutcome.Implemented
                || !IsValidImplementationInputChainSnapshot(snapshot, owner, orderedInputs, inputMessages!, runId, workspaceId))
            {
                return null;
            }

            var originalProposal = parent;
            var proposalOwner = ResolveSnapshotOwningAttempt(snapshot, parent, runId, AgentRole.Planner)
                ?? ResolveSnapshotOwningAttempt(snapshot, parent, runId, AgentRole.Resolver);
            if (proposalOwner?.AgentRole == AgentRole.Resolver
                && parent.InReplyToMessageId is { } originalProposalId
                && snapshot.MessagesById.TryGetValue(originalProposalId, out var resolvedOriginalProposal))
            {
                originalProposal = resolvedOriginalProposal;
            }

            return new Result(owner, executionReport, originalProposal, null, [], []);
        }

        if (outcome != AgentOutcome.CorrectionApplied
            || orderedInputs.Count < 2
            || !HasExactContiguousInputSequence(orderedInputs)
            || inputMessages.Length != orderedInputs.Count)
        {
            return null;
        }

        var previousExecutionReport = inputMessages[0];
        if (previousExecutionReport is null || previousExecutionReport.Type != CollaborationMessageType.ExecutionReport)
        {
            return null;
        }

        var previousOwner = ResolveSnapshotOwningAttempt(snapshot, previousExecutionReport, runId, AgentRole.Implementer);
        if (previousOwner is null
            || previousOwner.AgentGitWorkspaceId != workspaceId
            || previousOwner.AgentResultGitCheckpointId != owner.AgentGitCheckpointId)
        {
            return null;
        }

        var previousChain = ResolveSnapshotCore(
            snapshot, previousExecutionReport, runId, workspaceId, owner.AgentGitCheckpointId!.Value, visitedReports);
        if (previousChain is null || parent.Id != previousChain.OriginalProposal.Id)
        {
            return null;
        }

        var orderedFindings = new List<CollaborationMessage>(orderedInputs.Count - 1);
        Attempt? reviewAttempt = null;
        for (var index = 1; index < orderedInputs.Count; index++)
        {
            var finding = inputMessages[index];
            if (finding is null
                || finding.Type != CollaborationMessageType.ReviewFinding
                || finding.InReplyToMessageId != previousExecutionReport.Id
                || finding.Sequence <= previousExecutionReport.Sequence
                || finding.Provenance != CollaborationMessageProvenance.ProviderObserved
                || finding.AttemptId is not { } findingAttemptId)
            {
                return null;
            }

            var findingOwner = ResolveSnapshotOwningAttempt(snapshot, finding, runId, AgentRole.CodeReviewer);
            if (findingOwner is null
                || findingOwner.Status != AttemptStatus.Completed
                || findingOwner.AgentResponseContract != AgentResponseContract.ImplementationReview
                || findingOwner.AgentOutcome != AgentOutcome.ReviewChangesRequested
                || findingOwner.AgentGitWorkspaceId != workspaceId
                || findingOwner.AgentGitCheckpointId != owner.AgentGitCheckpointId
                || findingOwner.Id != findingAttemptId)
            {
                return null;
            }

            reviewAttempt ??= findingOwner;
            if (reviewAttempt.Id != findingOwner.Id)
            {
                return null;
            }

            orderedFindings.Add(finding);
        }

        if (reviewAttempt is null)
        {
            return null;
        }

        var reviewInputs = snapshot.InputsFor(reviewAttempt.Id);
        var reviewFindings = snapshot.Messages
            .Where(message => message.AttemptId == reviewAttempt.Id
                && message.Type == CollaborationMessageType.ReviewFinding)
            .OrderBy(message => message.Sequence)
            .ToArray();
        if (reviewInputs.Count != 1
            || reviewInputs[0].Sequence != 0
            || reviewInputs[0].CollaborationMessageId != previousExecutionReport.Id
            || !reviewFindings.Select(finding => finding.Id).SequenceEqual(orderedFindings.Select(finding => finding.Id)))
        {
            return null;
        }

        var orderedRevisionResponses = snapshot.Messages
            .Where(message => message.AttemptId == owner.Id && message.Type == CollaborationMessageType.RevisionResponse)
            .OrderBy(message => message.Sequence)
            .ToArray();
        if (orderedRevisionResponses.Length != orderedFindings.Count
            || orderedRevisionResponses.Select(message => message.InReplyToMessageId).Distinct().Count() != orderedFindings.Count
            || orderedRevisionResponses.Any(message => message.InReplyToMessageId is null)
            || !orderedFindings.Select(finding => finding.Id)
                .SequenceEqual(orderedRevisionResponses.Select(message => message.InReplyToMessageId!.Value))
            || !orderedRevisionResponses.Zip(orderedFindings)
                .All(pair => pair.First.Sequence > pair.Second.Sequence)
            || orderedRevisionResponses.Any(message =>
                ResolveSnapshotOwningAttempt(snapshot, message, runId, AgentRole.Implementer)?.Id != owner.Id))
        {
            return null;
        }

        if (orderedRevisionResponses.Any(response => executionReport.Sequence <= response.Sequence))
        {
            return null;
        }

        return new Result(owner, executionReport, previousChain.OriginalProposal, previousExecutionReport,
            orderedFindings, orderedRevisionResponses);
    }

    private static bool IsValidImplementationInputChainSnapshot(
        Snapshot snapshot,
        Attempt implementationAttempt,
        IReadOnlyList<AttemptInputMessage> orderedInputs,
        IReadOnlyList<CollaborationMessage?> inputMessages,
        Guid runId,
        Guid workspaceId)
    {
        if (!HasExactContiguousInputSequence(orderedInputs)
            || orderedInputs.Count < 2
            || inputMessages.Count != orderedInputs.Count
            || inputMessages[0] is not { } proposal
            || proposal.RunId != runId
            || proposal.Type != CollaborationMessageType.Proposal)
        {
            return false;
        }

        var proposalOwner = ResolveSnapshotOwningAttempt(snapshot, proposal, runId, AgentRole.Planner)
            ?? ResolveSnapshotOwningAttempt(snapshot, proposal, runId, AgentRole.Resolver);
        if (proposalOwner is null
            || proposalOwner.Status != AttemptStatus.Completed
            || proposalOwner.AgentGitWorkspaceId != workspaceId
            || proposalOwner.AgentGitCheckpointId != implementationAttempt.AgentGitCheckpointId
            || !IsValidProposalAttempt(proposalOwner))
        {
            return false;
        }

        var remainingMessages = inputMessages.Skip(1).Select(message => message!).ToArray();

        if (proposalOwner.AgentRole == AgentRole.Planner)
        {
            if (remainingMessages.Any(message => message.RunId != runId || message.Type != CollaborationMessageType.Acceptance)
                || remainingMessages.Length != 1)
            {
                return false;
            }

            var acceptance = remainingMessages[0];
            var acceptanceOwner = ResolveSnapshotOwningAttempt(snapshot, acceptance, runId, AgentRole.CriticalReviewer);
            if (acceptanceOwner is null
                || acceptanceOwner.Status != AttemptStatus.Completed
                || acceptanceOwner.AgentResponseContract != AgentResponseContract.CriticalReview
                || acceptanceOwner.AgentOutcome != AgentOutcome.Accepted
                || acceptanceOwner.AgentGitWorkspaceId != workspaceId
                || acceptanceOwner.AgentGitCheckpointId != implementationAttempt.AgentGitCheckpointId
                || acceptance.InReplyToMessageId != proposal.Id)
            {
                return false;
            }

            var acceptanceInputs = snapshot.InputsFor(acceptanceOwner.Id);
            var acceptanceOutputs = snapshot.Messages
                .Where(message => message.AttemptId == acceptanceOwner.Id)
                .ToArray();
            return acceptanceInputs.Count == 1
                && acceptanceInputs[0].Sequence == 0
                && acceptanceInputs[0].CollaborationMessageId == proposal.Id
                && acceptanceOutputs.Length == 1
                && acceptanceOutputs[0].Id == acceptance.Id
                && acceptanceOutputs[0].Type == CollaborationMessageType.Acceptance
                && acceptanceOutputs[0].Provenance == CollaborationMessageProvenance.ProviderObserved
                && acceptance.Sequence > proposal.Sequence;
        }

        if (proposalOwner.AgentRole != AgentRole.Resolver
            || proposalOwner.AgentResponseContract != AgentResponseContract.ChallengeResolution
            || proposalOwner.AgentOutcome != AgentOutcome.Resolved
            || proposal.InReplyToMessageId is not { } originalProposalId
            || !snapshot.MessagesById.TryGetValue(originalProposalId, out var originalProposal)
            || originalProposal.Type != CollaborationMessageType.Proposal
            || proposal.Sequence <= originalProposal.Sequence)
        {
            return false;
        }

        var plannerOwner = ResolveSnapshotOwningAttempt(snapshot, originalProposal, runId, AgentRole.Planner);
        if (plannerOwner is null
            || plannerOwner.Status != AttemptStatus.Completed
            || plannerOwner.AgentGitWorkspaceId != workspaceId
            || plannerOwner.AgentGitCheckpointId != implementationAttempt.AgentGitCheckpointId
            || plannerOwner.AgentResponseContract != AgentResponseContract.Proposal
            || plannerOwner.AgentOutcome != AgentOutcome.Proposed
            || snapshot.Messages.Count(message => message.AttemptId == plannerOwner.Id
                && message.Type == CollaborationMessageType.Proposal
                && message.Provenance == CollaborationMessageProvenance.ProviderObserved) != 1)
        {
            return false;
        }

        var reviewAttempts = snapshot.AttemptsById.Values
            .Where(attempt => attempt.RunId == runId
                && attempt.Kind == AttemptKind.Agent
                && attempt.AgentRole == AgentRole.CriticalReviewer
                && attempt.AgentResponseContract == AgentResponseContract.CriticalReview
                && attempt.Status == AttemptStatus.Completed
                && attempt.AgentOutcome == AgentOutcome.Challenged
                && attempt.AgentGitWorkspaceId == workspaceId
                && attempt.AgentGitCheckpointId == implementationAttempt.AgentGitCheckpointId)
            .ToArray();
        var resolverInputs = snapshot.InputsFor(proposalOwner.Id);
        if (resolverInputs.Count < 2
            || !HasExactContiguousInputSequence(resolverInputs)
            || resolverInputs[0].CollaborationMessageId != originalProposal.Id)
        {
            return false;
        }

        var challengeIds = resolverInputs.Skip(1).Select(input => input.CollaborationMessageId).ToArray();
        if (challengeIds.Distinct().Count() != challengeIds.Length)
        {
            return false;
        }

        var reviewMatches = reviewAttempts.Where(reviewAttempt =>
        {
            var reviewInputs = snapshot.InputsFor(reviewAttempt.Id);
            var challenges = snapshot.Messages
                .Where(message => message.AttemptId == reviewAttempt.Id)
                .OrderBy(message => message.Sequence)
                .ToArray();
            return reviewInputs.Count == 1
                && reviewInputs[0].Sequence == 0
                && reviewInputs[0].CollaborationMessageId == originalProposal.Id
                && challenges.Length == challengeIds.Length
                && challenges.All(challenge => challenge.Type == CollaborationMessageType.Challenge
                    && challenge.Provenance == CollaborationMessageProvenance.ProviderObserved
                    && challenge.RunId == runId
                    && challenge.InReplyToMessageId == originalProposal.Id
                    && challenge.Sequence > originalProposal.Sequence
                    && ResolveSnapshotOwningAttempt(snapshot, challenge, runId, AgentRole.CriticalReviewer)?.Id == reviewAttempt.Id)
                && challenges.Select(challenge => challenge.Id).SequenceEqual(challengeIds);
        }).ToArray();
        if (reviewMatches.Length != 1)
        {
            return false;
        }

        var resolverDecisions = snapshot.Messages
            .Where(message => message.AttemptId == proposalOwner.Id && message.Type == CollaborationMessageType.Decision)
            .OrderBy(message => message.Sequence)
            .ToArray();
        if (resolverDecisions.Length != challengeIds.Length
            || remainingMessages.Any(message => message.RunId != runId || message.Type != CollaborationMessageType.Decision)
            || !remainingMessages.Select(message => message.Id).SequenceEqual(resolverDecisions.Select(decision => decision.Id))
            || resolverDecisions.Any(decision => decision.Provenance != CollaborationMessageProvenance.ProviderObserved
                || ResolveSnapshotOwningAttempt(snapshot, decision, runId, AgentRole.Resolver)?.Id != proposalOwner.Id
                || decision.InReplyToMessageId is not { } challengeId
                || !snapshot.MessagesById.TryGetValue(challengeId, out var challenge)
                || challenge.Type != CollaborationMessageType.Challenge
                || decision.Sequence <= challenge.Sequence)
            || !resolverDecisions.Select(decision => decision.InReplyToMessageId).SequenceEqual(challengeIds.Select(id => (Guid?)id)))
        {
            return false;
        }

        var resolverProposals = snapshot.Messages
            .Where(message => message.AttemptId == proposalOwner.Id
                && message.Type == CollaborationMessageType.Proposal
                && message.Provenance == CollaborationMessageProvenance.ProviderObserved)
            .ToArray();
        return resolverProposals.Length == 1
            && resolverProposals[0].Id == proposal.Id
            && resolverProposals[0].InReplyToMessageId == originalProposal.Id
            && resolverProposals[0].Sequence > resolverDecisions[^1].Sequence;
    }

    private static Attempt? ResolveSnapshotOwningAttempt(
        Snapshot snapshot, CollaborationMessage message, Guid runId, AgentRole expectedRole)
    {
        if (message.RunId != runId
            || message.Provenance != CollaborationMessageProvenance.ProviderObserved
            || message.AttemptId is not { } attemptId
            || !snapshot.AttemptsById.TryGetValue(attemptId, out var attempt)
            || attempt.RunId != runId
            || attempt.Kind != AttemptKind.Agent
            || attempt.AgentRole != expectedRole
            || attempt.AgentResponseContract is not { } responseContract
            || AgentAttemptContract.For(responseContract).Role != expectedRole
            || attempt.AgentProvider is not { } provider
            || !Enum.IsDefined(provider)
            || message.Actor != ParticipantIdentity.ForAgent(expectedRole, provider))
        {
            return null;
        }

        return attempt;
    }

    private static bool HasExactContiguousInputSequence(IReadOnlyList<AttemptInputMessage> orderedInputs) =>
        orderedInputs.Count > 0
        && orderedInputs[0].Sequence == 0
        && orderedInputs.Select((input, index) => input.Sequence == index).All(isContiguous => isContiguous)
        && orderedInputs.Select(input => input.CollaborationMessageId).Distinct().Count() == orderedInputs.Count;

    private static bool IsValidProposalAttempt(Attempt attempt) =>
        (attempt.AgentRole, attempt.AgentResponseContract, attempt.AgentOutcome) is
            (AgentRole.Planner, AgentResponseContract.Proposal, AgentOutcome.Proposed)
            or (AgentRole.Resolver, AgentResponseContract.ChallengeResolution, AgentOutcome.Resolved);

    private static bool IsValidReportPair(AgentResponseContract contract, AgentOutcome outcome) =>
        (contract, outcome) is
            (AgentResponseContract.ImplementationReport, AgentOutcome.Implemented)
            or (AgentResponseContract.ReviewCorrection, AgentOutcome.CorrectionApplied);
}
