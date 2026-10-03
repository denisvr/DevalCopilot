using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies;

/// <summary>
/// The one Application-owned rule for the explicit human authorization of one implementation claim for the final
/// (depth-two) Proposal of a planning lineage (ADR-0016). Everything here is decided from durable identity in one
/// run-scoped snapshot, never from text, and fails closed without echoing stored content:
/// <list type="bullet">
/// <item>the <b>source</b>: a host-constructed, attemptless, protocol-1.0 Orchestrator-to-Human escalation that is the
/// only escalation replying to a complete, coherent depth-two lineage's final Proposal, whose summary and content are
/// exactly one of the two complete canonical records the second resolution has written (<see cref="PlanningEscalation"/>
/// now, its historical form before ADR-0020), recomputed from the verified identifiers and compared ordinally;</item>
/// <item>the <b>recorded grant</b>: a relation row bound to that source, its final Proposal and an exact workspace,
/// starting checkpoint and fingerprint, whose HumanInstruction is a human-submitted, attemptless message replying to
/// the escalation in the canonical form <see cref="PlanningImplementationInstruction"/> writes, and whose consuming
/// attempt (if any) holds exactly the ordered inputs final Proposal, second-round Decisions, authorization;</item>
/// <item><b>currency</b> (a separate question, asked only when a grant is about to be created or spent): the grant's
/// workspace, checkpoint and fingerprint are the current ones and no newer independent Planner Proposal exists.</item>
/// </list>
/// Historical validity (a report chain) deliberately ignores currency: a later independent Planner Proposal or a later
/// checkpoint never invalidates an implementation that was authorized and claimed against its own starting state.
/// </summary>
internal static class PlanningImplementationAuthorizationEvidence
{
    internal enum SourceFailure
    {
        NotFound,
        Invalid,
        Stale,
    }

    internal sealed record SourceEvaluation(
        PlanningLineage.Node? Final, CollaborationMessage? Escalation, SourceFailure? Failure)
    {
        public static SourceEvaluation Success(PlanningLineage.Node final, CollaborationMessage escalation) =>
            new(final, escalation, null);

        public static SourceEvaluation Fail(SourceFailure failure) => new(null, null, failure);
    }

    /// <summary>A grant proven coherent with its whole recorded chain, including the exact rationale.</summary>
    internal sealed record Recorded(
        PlanningImplementationAuthorization Grant,
        CollaborationMessage Escalation,
        CollaborationMessage Instruction,
        string Rationale,
        PlanningLineage.Node Final)
    {
        /// <summary>The exact ordered attempt inputs a claim consuming this grant records.</summary>
        public IReadOnlyList<Guid> ExpectedInputMessageIds { get; } =
            [Final.Proposal.Id, .. Final.Decisions.Select(decision => decision.Id), Instruction.Id];
    }

    /// <summary>How an implementation attempt relates to this concept: a historical or ordinary plan form that must hold
    /// no grant, a validly authorized form, or an incoherent mixture (never dispatched or accepted).</summary>
    internal enum AttemptForm
    {
        Ordinary,
        Authorized,
        Invalid,
    }

    internal sealed record AttemptClassification(AttemptForm Form, Recorded? Recorded);

    public static SourceEvaluation EvaluateSource(
        ImplementerExecutionReportEligibility.Snapshot snapshot,
        Guid runId,
        Guid workspaceId,
        Guid checkpointId,
        string? fingerprintSha256,
        Guid escalationMessageId)
    {
        if (!snapshot.MessagesById.TryGetValue(escalationMessageId, out var escalation)
            || escalation.RunId != runId
            || escalation.Type != CollaborationMessageType.Escalation)
        {
            return SourceEvaluation.Fail(SourceFailure.NotFound);
        }

        if (escalation.Provenance != CollaborationMessageProvenance.HostConstructed
            || escalation.AttemptId is not null
            || escalation.Actor != ParticipantIdentity.ForOrchestrator()
            || escalation.Recipient != ParticipantIdentity.ForHuman()
            || !string.Equals(escalation.ProtocolVersion, CollaborationMessage.ProtocolVersionOne, StringComparison.Ordinal)
            || escalation.InReplyToMessageId is not { } finalProposalId)
        {
            return SourceEvaluation.Fail(SourceFailure.Invalid);
        }

        var evaluation = PlanningLineage.Evaluate(
            snapshot, runId, workspaceId, checkpointId, fingerprintSha256, finalProposalId);
        if (evaluation.Node is not { } final)
        {
            return SourceEvaluation.Fail(
                evaluation.Failure == PlanningLineage.FailureKind.CheckpointStale ? SourceFailure.Stale : SourceFailure.Invalid);
        }

        if (final.Depth != PlanningLineage.MaximumDepth
            || final.Parent is not { } firstRevision
            || !PlanningLineage.OwnsExactlyOneProposal(snapshot, final.Root.Owner))
        {
            return SourceEvaluation.Fail(SourceFailure.Invalid);
        }

        // Exactly one escalation may answer the final Proposal; a second one, canonical or not, is ambiguous evidence.
        var escalationsOfFinal = snapshot.Messages.Count(message =>
            message.Type == CollaborationMessageType.Escalation && message.InReplyToMessageId == finalProposalId);
        if (escalationsOfFinal != 1
            || escalation.Sequence <= final.Proposal.Sequence
            || !string.Equals(escalation.Summary, PlanningEscalation.Summary, StringComparison.Ordinal)
            || !PlanningEscalation.IsCanonicalContent(
                escalation.StructuredContentJson,
                final.Root.Proposal.Id,
                firstRevision.Proposal.Id,
                final.Proposal.Id,
                final.Challenges.Select(challenge => challenge.Id).ToArray()))
        {
            return SourceEvaluation.Fail(SourceFailure.Invalid);
        }

        return SourceEvaluation.Success(final, escalation);
    }

    /// <summary>Whether a newer independent provider-observed Planner Proposal exists: a later root replaces the whole
    /// lineage, so its final plan and escalation are no longer the run's current planning state.</summary>
    public static bool IsSuperseded(ImplementerExecutionReportEligibility.Snapshot snapshot, PlanningLineage.Node final) =>
        snapshot.Messages.Any(message =>
            message.Type == CollaborationMessageType.Proposal
            && message.Provenance == CollaborationMessageProvenance.ProviderObserved
            && message.ActorAgentRole == AgentRole.Planner
            && message.Sequence > final.Root.Proposal.Sequence);

    /// <summary>Whether the grant's bound workspace, checkpoint and fingerprint are exactly the given current ones.</summary>
    public static bool IsBoundTo(
        PlanningImplementationAuthorization grant, Guid workspaceId, Guid checkpointId, string fingerprintSha256) =>
        grant.WorkspaceId == workspaceId
        && grant.CheckpointId == checkpointId
        && string.Equals(grant.FingerprintSha256, fingerprintSha256, StringComparison.Ordinal);

    public static Recorded? ValidateRecorded(
        ImplementerExecutionReportEligibility.Snapshot snapshot, PlanningImplementationAuthorization grant)
    {
        var source = EvaluateSource(
            snapshot, grant.RunId, grant.WorkspaceId, grant.CheckpointId, grant.FingerprintSha256, grant.EscalationMessageId);
        if (source.Final is not { } final
            || source.Escalation is not { } escalation
            || final.Proposal.Id != grant.FinalProposalMessageId)
        {
            return null;
        }

        if (!snapshot.MessagesById.TryGetValue(grant.HumanInstructionMessageId, out var instruction)
            || instruction.RunId != grant.RunId
            || instruction.Type != CollaborationMessageType.HumanInstruction
            || instruction.Provenance != CollaborationMessageProvenance.HumanSubmitted
            || instruction.AttemptId is not null
            || instruction.Actor != ParticipantIdentity.ForHuman()
            || instruction.Recipient != ParticipantIdentity.ForOrchestrator()
            || instruction.InReplyToMessageId != escalation.Id
            || instruction.Sequence <= escalation.Sequence
            || !string.Equals(instruction.ProtocolVersion, CollaborationMessage.ProtocolVersionOne, StringComparison.Ordinal)
            || !string.Equals(instruction.Summary, PlanningImplementationInstruction.Summary, StringComparison.Ordinal)
            || PlanningImplementationInstruction.TryReadRationale(instruction.StructuredContentJson) is not { } rationale)
        {
            return null;
        }

        var recorded = new Recorded(grant, escalation, instruction, rationale, final);
        return IsConsumptionCoherent(snapshot, recorded) ? recorded : null;
    }

    /// <summary>
    /// Classifies one implementation attempt against the grants of its run. An attempt is the authorized form when it
    /// consumed a grant, when its first input is a depth-two lineage Proposal, or when its last input is a HumanInstruction;
    /// it is then valid only if exactly one grant names it as consumer, that grant is itself coherent and bound to the
    /// attempt's own starting workspace, checkpoint and fingerprint, and the attempt's ordered inputs are exactly final
    /// Proposal, Decisions, authorization. Every other attempt is the ordinary form.
    /// </summary>
    public static AttemptClassification ClassifyAttempt(
        ImplementerExecutionReportEligibility.Snapshot snapshot, Attempt attempt)
    {
        var consumed = snapshot.PlanningAuthorizations.Where(grant => grant.ConsumedByAttemptId == attempt.Id).ToArray();
        var inputs = snapshot.InputsFor(attempt.Id);
        var firstInputId = inputs.Count > 0 ? inputs[0].CollaborationMessageId : (Guid?)null;
        var lastInputIsHumanInstruction = inputs.Count > 0
            && snapshot.MessagesById.TryGetValue(inputs[^1].CollaborationMessageId, out var lastInput)
            && lastInput.Type == CollaborationMessageType.HumanInstruction;

        var firstInputIsFinalPlan = false;
        if (firstInputId is { } planId
            && attempt.AgentGitWorkspaceId is { } workspaceId
            && attempt.AgentGitCheckpointId is { } checkpointId)
        {
            var lineage = PlanningLineage.Evaluate(
                snapshot, attempt.RunId, workspaceId, checkpointId, attempt.AgentCheckpointFingerprintSha256, planId);
            firstInputIsFinalPlan = lineage.Node is { Depth: PlanningLineage.MaximumDepth };
        }

        if (consumed.Length == 0 && !lastInputIsHumanInstruction && !firstInputIsFinalPlan)
        {
            return new AttemptClassification(AttemptForm.Ordinary, null);
        }

        if (consumed.Length != 1
            || firstInputId != consumed[0].FinalProposalMessageId
            || attempt.AgentGitWorkspaceId != consumed[0].WorkspaceId
            || attempt.AgentGitCheckpointId != consumed[0].CheckpointId
            || !string.Equals(attempt.AgentCheckpointFingerprintSha256, consumed[0].FingerprintSha256, StringComparison.Ordinal)
            || ValidateRecorded(snapshot, consumed[0]) is not { } recorded
            || recorded.Grant.ConsumedByAttemptId != attempt.Id)
        {
            return new AttemptClassification(AttemptForm.Invalid, null);
        }

        return new AttemptClassification(AttemptForm.Authorized, recorded);
    }

    private static bool IsConsumptionCoherent(ImplementerExecutionReportEligibility.Snapshot snapshot, Recorded recorded)
    {
        var grant = recorded.Grant;
        if (grant.ConsumedByAttemptId.HasValue != grant.ConsumedAtUtc.HasValue)
        {
            return false;
        }

        if (grant.ConsumedByAttemptId is not { } attemptId)
        {
            return true;
        }

        if (!snapshot.AttemptsById.TryGetValue(attemptId, out var consumer)
            || consumer.RunId != grant.RunId
            || consumer.Kind != AttemptKind.Agent
            || consumer.AgentRole != AgentRole.Implementer
            || consumer.AgentResponseContract != AgentResponseContract.ImplementationReport
            || consumer.AgentProvider != AgentProvider.ClaudeCode
            || consumer.AgentGitWorkspaceId != grant.WorkspaceId
            || consumer.AgentGitCheckpointId != grant.CheckpointId
            || !string.Equals(consumer.AgentCheckpointFingerprintSha256, grant.FingerprintSha256, StringComparison.Ordinal)
            || consumer.ClaimedAtUtc < grant.CreatedAtUtc)
        {
            return false;
        }

        var inputs = snapshot.InputsFor(attemptId);
        return ImplementerExecutionReportEligibility.HasExactContiguousInputSequence(inputs)
            && inputs.Select(input => input.CollaborationMessageId).SequenceEqual(recorded.ExpectedInputMessageIds);
    }
}
