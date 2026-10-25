using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

/// <summary>
/// ADR-0065 Domain facts: the dispatch witness (exact claimed operation of one family, queued admission exactly when queued), the
/// physical termination observation (physical versus semantic exit code, durations, never a success) and the Human release receipt with
/// its typed intent snapshot (ADR-0066), plus the versioned canonical confirmation. They are plain immutable records of what was proved;
/// none of them changes a result.
/// </summary>
public sealed class LocalExecutionOwnershipReleaseDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Sha = new('a', 64);

    private static (LocalExecutionSequence Sequence, LocalExecutionSequenceStep Step, Guid OperationId) Claimed(
        LocalExecutionStepKind kind = LocalExecutionStepKind.Implementation, bool queued = false)
    {
        var sequence = LocalExecutionSequence.Arm(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Sha, Guid.NewGuid(),
            LocalExecutionPlanAuthority.AcceptedRoot, Sha, Sha, "{}", 2, 2, Now, 1,
            queued ? LocalExecutionAdmissionMode.WaitForAdmission : LocalExecutionAdmissionMode.Immediate);
        var isImplementation = kind == LocalExecutionStepKind.Implementation;
        var isVerification = kind == LocalExecutionStepKind.Verification;
        var step = LocalExecutionSequenceStep.Reserve(
            Guid.NewGuid(), sequence.Id, isImplementation ? 0 : 2, kind, isImplementation ? 0 : 1, isVerification ? 0 : null,
            isImplementation ? null : Guid.NewGuid(), Guid.NewGuid(), Sha, isImplementation ? null : Guid.NewGuid(),
            LocalExecutionSequencePolicy.IsCorrectionKind(kind) ? Guid.NewGuid() : null, Guid.NewGuid(), Guid.NewGuid(), Now);
        step.MarkClaimed(step.ReservedOperationId, Now, Now.AddMinutes(5));
        return (sequence, step, step.ReservedOperationId);
    }

    private static LocalExecutionDispatchWitness Witness(
        LocalExecutionSequence sequence, LocalExecutionSequenceStep step, Guid operationId, Guid? admission = null)
    {
        var contract = LocalExecutionSequencePolicy.ContractOf(step.Kind);
        return contract is { } known
            ? LocalExecutionDispatchWitness.RecordAgent(Guid.NewGuid(), sequence, step, operationId, known, admission, Now, Guid.NewGuid(), Guid.NewGuid())
            : LocalExecutionDispatchWitness.RecordVerification(
                Guid.NewGuid(), sequence, step, operationId, Guid.NewGuid(), admission, Now, Guid.NewGuid(), Guid.NewGuid());
    }

    // ---- witness ---------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(LocalExecutionStepKind.Implementation)]
    [InlineData(LocalExecutionStepKind.CodeReview)]
    [InlineData(LocalExecutionStepKind.VerificationDiagnosis)]
    [InlineData(LocalExecutionStepKind.ReviewCorrection)]
    [InlineData(LocalExecutionStepKind.DiagnosisCorrection)]
    public void An_agent_witness_pins_the_exact_step_tuple_and_exactly_one_agent_identity(LocalExecutionStepKind kind)
    {
        var (sequence, step, operationId) = Claimed(kind);

        var witness = Witness(sequence, step, operationId);

        Assert.Equal(LocalExecutionOperationFamily.Agent, witness.OperationFamily);
        Assert.Equal(operationId, witness.AgentAttemptId);
        Assert.Null(witness.VerificationExecutionId);
        Assert.Equal(operationId, witness.OperationId);
        Assert.Equal(LocalExecutionSequencePolicy.ContractOf(kind), witness.AgentResponseContract);
        Assert.Null(witness.RecipeCommandId);
        Assert.Equal(step.Id, witness.StepId);
        Assert.Equal((step.Ordinal, step.PassNumber, step.RecipeOrdinal), (witness.Ordinal, witness.PassNumber, witness.RecipeOrdinal));
        Assert.Equal((step.SourceCheckpointId, step.SourceFingerprintSha256), (witness.SourceCheckpointId, witness.SourceFingerprintSha256));
        Assert.Equal((sequence.RunId, sequence.ProjectId, sequence.WorkspaceId), (witness.RunId, witness.ProjectId, witness.WorkspaceId));
        Assert.Equal(LocalExecutionDispatchWitness.CurrentVersion, witness.Version);
    }

    [Fact]
    public void A_verification_witness_pins_its_recipe_and_no_agent_identity()
    {
        var (sequence, step, operationId) = Claimed(LocalExecutionStepKind.Verification);

        var witness = Witness(sequence, step, operationId);

        Assert.Equal(LocalExecutionOperationFamily.Verification, witness.OperationFamily);
        Assert.Equal(operationId, witness.VerificationExecutionId);
        Assert.Null(witness.AgentAttemptId);
        Assert.Null(witness.AgentResponseContract);
        Assert.NotNull(witness.RecipeCommandId);
        Assert.Equal(step.RecipeOrdinal, witness.RecipeOrdinal);
    }

    [Fact]
    public void A_witness_names_a_consumed_admission_exactly_when_the_sequence_is_queued()
    {
        var (queued, queuedStep, queuedOperation) = Claimed(queued: true);
        var (immediate, immediateStep, immediateOperation) = Claimed();

        Assert.NotNull(Witness(queued, queuedStep, queuedOperation, Guid.NewGuid()).AdmissionId);
        Assert.Throws<ArgumentException>(() => Witness(queued, queuedStep, queuedOperation, null));
        Assert.Null(Witness(immediate, immediateStep, immediateOperation).AdmissionId);
        Assert.Throws<ArgumentException>(() => Witness(immediate, immediateStep, immediateOperation, Guid.NewGuid()));
    }

    [Fact]
    public void A_witness_refuses_an_operation_that_is_not_the_exact_claimed_one_and_a_contract_that_does_not_match_the_kind()
    {
        var (sequence, step, operationId) = Claimed(LocalExecutionStepKind.CodeReview);
        var other = Claimed();

        Assert.Throws<ArgumentException>(() => Witness(sequence, step, Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => Witness(other.Sequence, step, operationId));
        Assert.Throws<ArgumentException>(() => LocalExecutionDispatchWitness.RecordAgent(
            Guid.NewGuid(), sequence, step, operationId, AgentResponseContract.ImplementationReport, null, Now, Guid.NewGuid(), Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => LocalExecutionDispatchWitness.RecordAgent(
            Guid.NewGuid(), sequence, step, operationId, AgentResponseContract.ImplementationReview, null, Now, Guid.Empty, Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => LocalExecutionDispatchWitness.RecordAgent(
            Guid.NewGuid(), sequence, step, operationId, AgentResponseContract.ImplementationReview, null, Now, Guid.NewGuid(), Guid.Empty));
        Assert.Throws<ArgumentException>(() => LocalExecutionDispatchWitness.RecordVerification(
            Guid.NewGuid(), sequence, step, operationId, Guid.NewGuid(), null, Now, Guid.NewGuid(), Guid.NewGuid()));
    }

    // ---- observation: physical versus semantic ---------------------------------------------------------------------------------

    [Theory]
    [InlineData(ProcessOutcome.TimedOut, null, 137, 4900, 5000, true)]
    [InlineData(ProcessOutcome.Cancelled, null, 1, 0, 5000, true)]
    [InlineData(ProcessOutcome.Exited, 3, 3, 10, 10, true)]
    [InlineData(ProcessOutcome.Exited, 0, 0, 5000, 5000, true)]
    [InlineData(ProcessOutcome.TimedOut, 1, 1, 10, 10, false)]
    [InlineData(ProcessOutcome.Exited, null, 1, 10, 10, false)]
    [InlineData(ProcessOutcome.Exited, 1, 2, 10, 10, false)]
    [InlineData(ProcessOutcome.TimedOut, null, 137, 5001, 5000, false)]
    [InlineData(ProcessOutcome.TimedOut, null, 137, -1, 5000, false)]
    [InlineData((ProcessOutcome)99, null, 137, 1, 5000, false)]
    public void A_physical_exit_is_coherent_with_the_returned_result_only_by_the_closed_rule(
        ProcessOutcome outcome, int? semantic, int physical, int elapsedMilliseconds, int durationMilliseconds, bool coherent)
    {
        Assert.Equal(
            coherent,
            LocalExecutionTerminationObservation.IsCoherent(
                outcome, semantic, TimeSpan.FromMilliseconds(durationMilliseconds), physical, TimeSpan.FromMilliseconds(elapsedMilliseconds)));
    }

    [Fact]
    public void A_timeout_observation_keeps_its_outcome_and_no_semantic_exit_code_and_is_never_a_success()
    {
        var (sequence, step, operationId) = Claimed();
        var witness = Witness(sequence, step, operationId);

        var observation = LocalExecutionTerminationObservation.Record(
            Guid.NewGuid(), witness, ProcessOutcome.TimedOut, null, TimeSpan.FromSeconds(5), 137, TimeSpan.FromSeconds(4), Now.AddSeconds(5));

        Assert.Equal(witness.Id, observation.WitnessId);
        Assert.Equal(ProcessOutcome.TimedOut, observation.ProcessOutcome);
        Assert.Null(observation.SemanticExitCode);
        Assert.Equal(137, observation.PhysicalExitCode);
        Assert.Equal(TimeSpan.FromSeconds(4), observation.ObservedElapsed);
        Assert.Equal(TimeSpan.FromSeconds(5), observation.ProcessDuration);
        var evidence = AgentProcessExecutionEvidence.Create(ProcessOutcome.TimedOut, null, TimeSpan.FromSeconds(5));
        Assert.False(evidence.IsCleanExit);
    }

    [Fact]
    public void An_observation_refuses_a_contradiction_and_a_time_before_its_dispatch()
    {
        var (sequence, step, operationId) = Claimed();
        var witness = Witness(sequence, step, operationId);

        Assert.Throws<ArgumentException>(() => LocalExecutionTerminationObservation.Record(
            Guid.NewGuid(), witness, ProcessOutcome.Exited, 1, TimeSpan.FromSeconds(5), 2, TimeSpan.FromSeconds(4), Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => LocalExecutionTerminationObservation.Record(
            Guid.NewGuid(), witness, ProcessOutcome.TimedOut, null, TimeSpan.FromSeconds(5), 137, TimeSpan.FromSeconds(4), Now.AddSeconds(-1)));
        Assert.Throws<ArgumentException>(() => LocalExecutionTerminationObservation.Record(
            Guid.Empty, witness, ProcessOutcome.TimedOut, null, TimeSpan.FromSeconds(5), 137, TimeSpan.FromSeconds(4), Now));
    }

    // ---- release ---------------------------------------------------------------------------------------------------------------

    private static (LocalExecutionDispatchWitness Witness, LocalExecutionTerminationObservation Observation) Observed(
        LocalExecutionStepKind kind = LocalExecutionStepKind.Implementation)
    {
        var (sequence, step, operationId) = Claimed(kind);
        var witness = Witness(sequence, step, operationId);
        var observation = LocalExecutionTerminationObservation.Record(
            Guid.NewGuid(), witness, ProcessOutcome.TimedOut, null, TimeSpan.FromSeconds(5), 137, TimeSpan.FromSeconds(4), Now.AddSeconds(5));
        return (witness, observation);
    }

    /// <summary>The coherent facts of a release of an Agent attempt that ended Failed with a timed-out process.</summary>
    private static LocalExecutionOwnershipReleaseFacts AgentFacts(
        LocalExecutionDispatchWitness witness, LocalExecutionTerminationObservation observation) => new(
        witness.Id,
        observation.Id,
        witness.OperationFamily,
        witness.OperationId,
        witness.SequenceId,
        witness.RunId,
        witness.ProjectId,
        witness.WorkspaceId,
        LocalExecutionSequenceState.NeedsAttention,
        witness.AdmissionId is null ? LocalExecutionAdmissionMode.Immediate : LocalExecutionAdmissionMode.WaitForAdmission,
        witness.AdmissionId,
        witness.StepId,
        witness.Kind,
        witness.Ordinal,
        witness.PassNumber,
        witness.RecipeOrdinal,
        witness.PredecessorStepId,
        witness.SourceCheckpointId,
        witness.SourceFingerprintSha256,
        witness.ReportMessageId,
        witness.SubjectAttemptId,
        witness.AgentResponseContract,
        witness.RecipeCommandId,
        "Failed",
        AgentOutcome.ProviderInvocationFailed,
        null,
        observation.ProcessOutcome,
        observation.SemanticExitCode,
        observation.ProcessDuration.Ticks,
        observation.PhysicalExitCode,
        observation.ObservedElapsed.Ticks,
        witness.DispatchedAtUtc,
        observation.RecordedAtUtc);

    private static LocalExecutionOwnershipRelease RecordRelease(
        LocalExecutionDispatchWitness witness, LocalExecutionTerminationObservation observation, LocalExecutionOwnershipReleaseFacts facts,
        DateTimeOffset releasedAt, DateTimeOffset expiry, string? sha = null, int version = 1) =>
        LocalExecutionOwnershipRelease.Record(
            Guid.NewGuid(), witness, observation, Guid.NewGuid(), releasedAt, version,
            sha ?? LocalExecutionOwnershipReleaseConfirmation.Digest(facts, expiry), expiry, facts);

    [Fact]
    public void A_release_names_its_witness_observation_step_event_and_the_exact_confirmation_and_snapshots_every_confirmed_fact()
    {
        var (witness, observation) = Observed();
        var releasedAt = Now.AddMinutes(1);
        var expiry = releasedAt.AddMinutes(1);
        var facts = AgentFacts(witness, observation);
        var digest = LocalExecutionOwnershipReleaseConfirmation.Digest(facts, expiry);

        var release = RecordRelease(witness, observation, facts, releasedAt, expiry);

        Assert.Equal((witness.Id, observation.Id, witness.StepId), (release.WitnessId, release.ObservationId, release.StepId));
        Assert.Equal((1, digest, expiry), (release.ConfirmationVersion, release.ConfirmationSha256, release.ConfirmationExpiresAtUtc));
        Assert.Equal(releasedAt, release.ReleasedAtUtc);
        Assert.Equal(1, release.IntentVersion);
        Assert.Equal((facts.Family, facts.OperationId, facts.SequenceId, facts.RunId), (release.IntentFamily, release.IntentOperationId, release.IntentSequenceId, release.IntentRunId));
        Assert.Equal((facts.ProjectId, facts.WorkspaceId, facts.SequenceState, facts.AdmissionMode), (release.IntentProjectId, release.IntentWorkspaceId, release.IntentSequenceState, release.IntentAdmissionMode));
        Assert.Equal((facts.Kind, facts.Ordinal, facts.PassNumber, facts.SourceCheckpointId), (release.IntentKind, release.IntentOrdinal, release.IntentPassNumber, release.IntentSourceCheckpointId));
        Assert.Equal((facts.SourceFingerprintSha256, facts.OperationStatus, facts.AgentOutcome, facts.CompletionFingerprintSha256), (release.IntentSourceFingerprintSha256, release.IntentOperationStatus, release.IntentAgentOutcome, release.IntentCompletionFingerprintSha256));
        Assert.Equal((facts.ProcessOutcome, facts.SemanticExitCode, facts.PhysicalExitCode), (release.IntentProcessOutcome, release.IntentSemanticExitCode, release.IntentPhysicalExitCode));
        Assert.Equal((facts.ProcessDurationTicks, facts.ObservedElapsedTicks), (release.IntentProcessDuration.Ticks, release.IntentObservedElapsed.Ticks));
        Assert.Equal((facts.DispatchedAtUtc, facts.RecordedAtUtc), (release.IntentDispatchedAtUtc, release.IntentObservationRecordedAtUtc));
        Assert.Equal((1, expiry), (release.IntentConfirmationVersion, release.IntentConfirmationExpiresAtUtc));
    }

    [Fact]
    public void A_verification_release_snapshots_the_concrete_completion_fingerprint_and_no_agent_outcome()
    {
        var (sequence, step, operationId) = Claimed(LocalExecutionStepKind.Verification);
        var witness = Witness(sequence, step, operationId);
        var observation = LocalExecutionTerminationObservation.Record(
            Guid.NewGuid(), witness, ProcessOutcome.Cancelled, null, TimeSpan.FromSeconds(5), 1, TimeSpan.FromSeconds(4), Now.AddSeconds(5));
        var completion = new string('9', 64);
        var facts = AgentFacts(witness, observation) with { OperationStatus = "SourceChanged", AgentOutcome = null, CompletionFingerprintSha256 = completion };

        var release = RecordRelease(witness, observation, facts, Now.AddMinutes(1), Now.AddMinutes(2));

        Assert.Equal(LocalExecutionOperationFamily.Verification, release.IntentFamily);
        Assert.Null(release.IntentAgentOutcome);
        Assert.Null(release.IntentAgentResponseContract);
        Assert.Equal(completion, release.IntentCompletionFingerprintSha256);
        Assert.Equal(witness.RecipeCommandId, release.IntentRecipeCommandId);
        Assert.Equal(ProcessOutcome.Cancelled, release.IntentProcessOutcome);
    }

    [Fact]
    public void A_release_refuses_a_foreign_observation_a_past_time_a_wrong_version_and_an_expired_or_overlong_confirmation()
    {
        var (witness, observation) = Observed();
        var (otherWitness, otherObservation) = Observed();
        var at = Now.AddMinutes(1);
        var facts = AgentFacts(witness, observation);

        Assert.Throws<ArgumentException>(() => RecordRelease(witness, otherObservation, facts, at, at.AddMinutes(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => RecordRelease(witness, observation, facts, observation.RecordedAtUtc.AddTicks(-1), at.AddMinutes(1)));
        Assert.Throws<ArgumentException>(() => RecordRelease(witness, observation, facts, at, at.AddMinutes(1), version: 2));
        Assert.Throws<ArgumentException>(() => RecordRelease(witness, observation, facts, at, at.AddMinutes(1), sha: new string('A', 64)));
        Assert.Throws<ArgumentException>(() => RecordRelease(witness, observation, facts, at, at, sha: Sha));
        Assert.Throws<ArgumentException>(() => RecordRelease(witness, observation, facts, at, at.AddMinutes(3)));
        // One tick beyond the two-minute lifetime is refused; exactly two minutes is accepted.
        Assert.Throws<ArgumentException>(() => RecordRelease(witness, observation, facts, at, at.AddMinutes(2).AddTicks(1)));
        Assert.NotNull(RecordRelease(witness, observation, facts, at, at.AddMinutes(2)));
        Assert.NotEqual(otherWitness.Id, witness.Id);
    }

    [Fact]
    public void A_release_refuses_a_digest_that_is_not_the_canonical_digest_of_the_facts_and_the_expiry()
    {
        var (witness, observation) = Observed();
        var at = Now.AddMinutes(1);
        var facts = AgentFacts(witness, observation);
        var other = facts with { AgentOutcome = AgentOutcome.SourceChanged };

        // A well-shaped digest of a different tuple or of a different expiry is not the confirmation of these facts.
        Assert.Throws<ArgumentException>(() => RecordRelease(
            witness, observation, facts, at, at.AddMinutes(1), LocalExecutionOwnershipReleaseConfirmation.Digest(other, at.AddMinutes(1))));
        Assert.Throws<ArgumentException>(() => RecordRelease(
            witness, observation, facts, at, at.AddMinutes(1), LocalExecutionOwnershipReleaseConfirmation.Digest(facts, at.AddMinutes(1).AddSeconds(1))));
        Assert.Throws<ArgumentException>(() => RecordRelease(witness, observation, facts, at, at.AddMinutes(1), Sha));
    }

    [Fact]
    public void A_release_refuses_facts_that_are_not_those_of_its_witness_and_observation_or_not_family_consistent()
    {
        var (witness, observation) = Observed();
        var at = Now.AddMinutes(1);
        var expiry = at.AddMinutes(1);
        var facts = AgentFacts(witness, observation);
        var verification = Observed(LocalExecutionStepKind.Verification);
        var verificationFacts = AgentFacts(verification.Witness, verification.Observation) with
        {
            OperationStatus = "TimedOut",
            AgentOutcome = null,
            CompletionFingerprintSha256 = Sha,
        };
        Assert.NotNull(RecordRelease(verification.Witness, verification.Observation, verificationFacts, at, expiry));

        var refused = new (LocalExecutionDispatchWitness Witness, LocalExecutionTerminationObservation Observation, LocalExecutionOwnershipReleaseFacts Facts)[]
        {
            (witness, observation, facts with { WitnessId = Guid.NewGuid() }),
            (witness, observation, facts with { ObservationId = Guid.NewGuid() }),
            (witness, observation, facts with { OperationId = Guid.NewGuid() }),
            (witness, observation, facts with { SequenceId = Guid.NewGuid() }),
            (witness, observation, facts with { Ordinal = facts.Ordinal + 1 }),
            (witness, observation, facts with { SourceFingerprintSha256 = new string('b', 64) }),
            (witness, observation, facts with { PhysicalExitCode = 1 }),
            (witness, observation, facts with { ObservedElapsedTicks = 1 }),
            (witness, observation, facts with { ProcessDurationTicks = 1 }),
            (witness, observation, facts with { ProcessOutcome = ProcessOutcome.Cancelled }),
            (witness, observation, facts with { ProcessOutcome = ProcessOutcome.Exited, SemanticExitCode = 137 }),
            (witness, observation, facts with { SemanticExitCode = 0 }),
            (witness, observation, facts with { DispatchedAtUtc = facts.DispatchedAtUtc.AddTicks(1) }),
            (witness, observation, facts with { RecordedAtUtc = facts.RecordedAtUtc.AddTicks(1) }),
            (witness, observation, facts with { SequenceState = LocalExecutionSequenceState.AwaitingStepResult }),
            (witness, observation, facts with { OperationStatus = " " }),
            (witness, observation, facts with { AgentOutcome = null }),
            (witness, observation, facts with { CompletionFingerprintSha256 = Sha }),
            (witness, observation, facts with { AgentResponseContract = null }),
            (verification.Witness, verification.Observation, verificationFacts with { CompletionFingerprintSha256 = null }),
            (verification.Witness, verification.Observation, verificationFacts with { CompletionFingerprintSha256 = "not-a-sha256" }),
            (verification.Witness, verification.Observation, verificationFacts with { CompletionFingerprintSha256 = new string('A', 64) }),
            (verification.Witness, verification.Observation, verificationFacts with { CompletionFingerprintSha256 = new string('a', 65) }),
            (verification.Witness, verification.Observation, verificationFacts with { AgentOutcome = AgentOutcome.SourceChanged }),
            (verification.Witness, verification.Observation, verificationFacts with { Family = LocalExecutionOperationFamily.Agent }),
        };
        foreach (var (candidateWitness, candidateObservation, candidateFacts) in refused)
        {
            Assert.Throws<ArgumentException>(() => RecordRelease(candidateWitness, candidateObservation, candidateFacts, at, expiry));
        }
    }

    // ---- confirmation ----------------------------------------------------------------------------------------------------------

    private static LocalExecutionOwnershipReleaseFacts Facts(LocalExecutionStepKind kind = LocalExecutionStepKind.CodeReview) => new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        LocalExecutionOperationFamily.Agent,
        Guid.Parse("33333333-3333-3333-3333-333333333333"),
        Guid.Parse("44444444-4444-4444-4444-444444444444"),
        Guid.Parse("55555555-5555-5555-5555-555555555555"),
        Guid.Parse("66666666-6666-6666-6666-666666666666"),
        Guid.Parse("77777777-7777-7777-7777-777777777777"),
        LocalExecutionSequenceState.NeedsAttention,
        LocalExecutionAdmissionMode.Immediate,
        null,
        Guid.Parse("88888888-8888-8888-8888-888888888888"),
        kind,
        3,
        1,
        null,
        Guid.Parse("99999999-9999-9999-9999-999999999999"),
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        Sha,
        Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
        null,
        AgentResponseContract.ImplementationReview,
        null,
        "Failed",
        AgentOutcome.ProviderInvocationFailed,
        null,
        ProcessOutcome.TimedOut,
        null,
        TimeSpan.FromSeconds(5).Ticks,
        137,
        TimeSpan.FromSeconds(4).Ticks,
        Now,
        Now.AddSeconds(5));

    [Fact]
    public void The_confirmation_digest_is_deterministic_versioned_lowercase_hex_and_changes_with_any_fact_or_the_expiry()
    {
        var facts = Facts();
        var expiry = Now.AddMinutes(2);
        var digest = LocalExecutionOwnershipReleaseConfirmation.Digest(facts, expiry);

        Assert.Equal(digest, LocalExecutionOwnershipReleaseConfirmation.Digest(facts with { }, expiry));
        Assert.True(LocalExecutionSequencePolicy.IsLowercaseSha256(digest));
        Assert.Equal(1, LocalExecutionOwnershipReleaseConfirmation.CurrentVersion);
        Assert.NotEqual(digest, LocalExecutionOwnershipReleaseConfirmation.Digest(facts, expiry.AddSeconds(1)));
        var changed = new[]
        {
            facts with { WitnessId = Guid.NewGuid() },
            facts with { ObservationId = Guid.NewGuid() },
            facts with { OperationId = Guid.NewGuid() },
            facts with { SequenceId = Guid.NewGuid() },
            facts with { StepId = Guid.NewGuid() },
            facts with { Ordinal = 4 },
            facts with { SourceFingerprintSha256 = new string('b', 64) },
            facts with { ProcessOutcome = ProcessOutcome.Cancelled },
            facts with { PhysicalExitCode = 1 },
            facts with { ObservedElapsedTicks = 1 },
            facts with { ProcessDurationTicks = 1 },
            facts with { DispatchedAtUtc = Now.AddTicks(1) },
            facts with { SequenceState = LocalExecutionSequenceState.Stopped },
            facts with { AdmissionMode = LocalExecutionAdmissionMode.WaitForAdmission, AdmissionId = Guid.NewGuid() },
            facts with { OperationStatus = "Interrupted" },
            facts with { AgentOutcome = AgentOutcome.SourceChanged },
            facts with { AgentOutcome = null, CompletionFingerprintSha256 = new string('9', 64) },
            facts with { CompletionFingerprintSha256 = new string('9', 64) },
            facts with { AgentOutcome = null, CompletionFingerprintSha256 = new string('8', 64) },
        };
        foreach (var candidate in changed)
        {
            Assert.NotEqual(digest, LocalExecutionOwnershipReleaseConfirmation.Digest(candidate, expiry));
        }
    }

    [Fact]
    public void The_expiry_is_at_most_two_minutes_after_the_read_and_a_client_cannot_extend_it()
    {
        var read = new DateTimeOffset(2026, 10, 23, 12, 0, 7, 654, TimeSpan.Zero);
        var expiry = LocalExecutionOwnershipReleaseConfirmation.ExpiryOf(read);

        Assert.True(expiry > read);
        Assert.True(expiry - read <= TimeSpan.FromMinutes(2));
        Assert.Equal(0, expiry.Ticks % TimeSpan.TicksPerSecond);
        Assert.True(LocalExecutionOwnershipReleaseConfirmation.IsLive(expiry, read));
        Assert.True(LocalExecutionOwnershipReleaseConfirmation.IsLive(expiry, expiry.AddTicks(-1)));
        Assert.False(LocalExecutionOwnershipReleaseConfirmation.IsLive(expiry, expiry));
        Assert.False(LocalExecutionOwnershipReleaseConfirmation.IsLive(expiry, expiry.AddSeconds(1)));
        Assert.False(LocalExecutionOwnershipReleaseConfirmation.IsLive(read.AddMinutes(10), read));
        Assert.False(LocalExecutionOwnershipReleaseConfirmation.IsLive(read.AddMinutes(1).ToOffset(TimeSpan.FromHours(2)), read));
    }

    [Theory]
    [InlineData("2026-10-23T12:00:00.0000000Z", true)]
    [InlineData("2026-10-23T12:00:00.1234567Z", true)]
    [InlineData("2026-10-23T12:00:00Z", false)]
    [InlineData("2026-10-23T12:00:00.0000000+00:00", false)]
    [InlineData("2026-10-23T12:00:00.0000000+01:00", false)]
    [InlineData(" 2026-10-23T12:00:00.0000000Z", false)]
    [InlineData("2026-10-23 12:00:00.0000000Z", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void The_canonical_time_form_is_exactly_the_utc_round_trip_spelling(string? text, bool canonical)
    {
        Assert.Equal(canonical, LocalExecutionOwnershipReleaseConfirmation.TryParseTime(text, out var value));
        if (canonical)
        {
            Assert.Equal(text, LocalExecutionOwnershipReleaseConfirmation.Time(value));
        }
    }

    [Fact]
    public void The_release_event_type_is_the_fixed_durable_name()
    {
        Assert.Equal("local_execution_sequence.ownership_released", RunEventType.LocalExecutionOwnershipReleased);
    }
}
