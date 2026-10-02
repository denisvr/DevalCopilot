using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

/// <summary>ADR-0018: the closed Domain facts of the verification-diagnosis attempt shape — its exact tuple, its claim
/// factory, its process-evidence classification, its completion transition, and its escalation row.</summary>
public sealed class VerificationDiagnosisDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    private const string Fingerprint = "fingerprint-diagnosis";

    private static Attempt Claim(string? model = null, string? effort = null, int slot = 1) =>
        Attempt.ClaimAgentVerificationDiagnosis(
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 65536, 131072, Now, model, effort, slot);

    private static Attempt ClaimDispatched()
    {
        var attempt = Claim();
        attempt.MarkAgentDispatched(Now.AddSeconds(1));
        return attempt;
    }

    [Fact]
    public void ClaimAgentVerificationDiagnosis_persists_the_exact_codex_read_only_code_reviewer_diagnosis_tuple()
    {
        var workspaceId = Guid.NewGuid();
        var checkpointId = Guid.NewGuid();
        var manifestId = Guid.NewGuid();
        var attempt = Attempt.ClaimAgentVerificationDiagnosis(
            Guid.NewGuid(), Guid.NewGuid(), 3, workspaceId, checkpointId, Fingerprint, manifestId,
            TimeSpan.FromMinutes(7), 4096, 8192, Now, "gpt-test", "high", 2);

        Assert.Equal(AttemptKind.Agent, attempt.Kind);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Equal(3, attempt.AttemptNumber);
        Assert.Equal(AgentProvider.Codex, attempt.AgentProvider);
        Assert.Equal(AgentRole.CodeReviewer, attempt.AgentRole);
        Assert.Equal(AgentResponseContract.VerificationDiagnosis, attempt.AgentResponseContract);
        Assert.Equal(CollaborationMessageType.ReviewFinding, attempt.AgentExpectedMessageType);
        Assert.Equal(CollaborationMessage.ProtocolVersionOne, attempt.AgentProtocolVersion);
        Assert.Equal(AgentPermissionProfile.ReadOnly, attempt.AgentPermissionProfile);
        Assert.Equal("codex-verification-diagnosis-v1", attempt.AgentAdapterContractVersion);
        Assert.Equal(VerificationDiagnosisPolicy.AdapterContractVersion, attempt.AgentAdapterContractVersion);
        Assert.Equal(workspaceId, attempt.AgentGitWorkspaceId);
        Assert.Equal(checkpointId, attempt.AgentGitCheckpointId);
        Assert.Equal(Fingerprint, attempt.AgentCheckpointFingerprintSha256);
        Assert.Equal(manifestId, attempt.AgentContextManifestArtifactId);
        Assert.Equal(TimeSpan.FromMinutes(7), attempt.AgentTimeout);
        Assert.Equal(4096, attempt.AgentMaxBytesPerStream);
        Assert.Equal(8192, attempt.AgentMaxTotalCapturedBytes);
        Assert.Equal("gpt-test", attempt.AgentRequestedModel);
        Assert.Equal("high", attempt.AgentRequestedEffort);
        Assert.Equal(2, attempt.AgentBudgetSlot);
        Assert.Null(attempt.AgentRepairSourceAttemptId);
        Assert.Null(attempt.AgentDispatchedAtUtc);
        Assert.Null(attempt.AgentOutcome);
        Assert.True(VerificationDiagnosisPolicy.HasExactTuple(attempt));
    }

    [Fact]
    public void ClaimAgentVerificationDiagnosis_accepts_a_blank_assignment_and_exposes_a_read_only_assignment_snapshot()
    {
        var snapshot = Claim().GetAssignmentSnapshot();

        Assert.NotNull(snapshot);
        Assert.Equal(AgentProvider.Codex, snapshot.Provider);
        Assert.Equal(AgentPermissionProfile.ReadOnly, snapshot.PermissionProfile);
        Assert.Equal("codex-verification-diagnosis-v1", snapshot.AdapterContractVersion);
        Assert.Null(snapshot.RequestedModel);
        Assert.Null(snapshot.RequestedEffort);
    }

    [Fact]
    public void ClaimAgentVerificationDiagnosis_rejects_every_invalid_argument()
    {
        var run = Guid.NewGuid();
        var ws = Guid.NewGuid();
        var cp = Guid.NewGuid();
        var manifest = Guid.NewGuid();
        var timeout = TimeSpan.FromMinutes(1);

        Assert.Throws<ArgumentOutOfRangeException>(() => Attempt.ClaimAgentVerificationDiagnosis(
            Guid.NewGuid(), run, 0, ws, cp, Fingerprint, manifest, timeout, 1, 1, Now, null, null, 1));
        Assert.Throws<ArgumentException>(() => Attempt.ClaimAgentVerificationDiagnosis(
            Guid.NewGuid(), run, 1, Guid.Empty, cp, Fingerprint, manifest, timeout, 1, 1, Now, null, null, 1));
        Assert.Throws<ArgumentException>(() => Attempt.ClaimAgentVerificationDiagnosis(
            Guid.NewGuid(), run, 1, ws, Guid.Empty, Fingerprint, manifest, timeout, 1, 1, Now, null, null, 1));
        Assert.Throws<ArgumentException>(() => Attempt.ClaimAgentVerificationDiagnosis(
            Guid.NewGuid(), run, 1, ws, cp, " ", manifest, timeout, 1, 1, Now, null, null, 1));
        Assert.Throws<ArgumentException>(() => Attempt.ClaimAgentVerificationDiagnosis(
            Guid.NewGuid(), run, 1, ws, cp, Fingerprint, Guid.Empty, timeout, 1, 1, Now, null, null, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Attempt.ClaimAgentVerificationDiagnosis(
            Guid.NewGuid(), run, 1, ws, cp, Fingerprint, manifest, TimeSpan.Zero, 1, 1, Now, null, null, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Attempt.ClaimAgentVerificationDiagnosis(
            Guid.NewGuid(), run, 1, ws, cp, Fingerprint, manifest, timeout, -1, 1, Now, null, null, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Attempt.ClaimAgentVerificationDiagnosis(
            Guid.NewGuid(), run, 1, ws, cp, Fingerprint, manifest, timeout, 1, -1, Now, null, null, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Attempt.ClaimAgentVerificationDiagnosis(
            Guid.NewGuid(), run, 1, ws, cp, Fingerprint, manifest, timeout, 1, 1, Now, null, null, 0));
        Assert.Throws<ArgumentException>(() => Attempt.ClaimAgentVerificationDiagnosis(
            Guid.NewGuid(), run, 1, ws, cp, Fingerprint, manifest, timeout, 1, 1, Now, null, "high", 1));
        Assert.Throws<ArgumentException>(() => Attempt.ClaimAgentVerificationDiagnosis(
            Guid.NewGuid(), run, 1, ws, cp, Fingerprint, manifest, timeout, 1, 1, Now, new string('m', 129), null, 1));
    }

    public static TheoryData<string, object?> WrongTupleMembers => new()
    {
        { nameof(Attempt.Kind), AttemptKind.Process },
        { nameof(Attempt.AgentProvider), AgentProvider.ClaudeCode },
        { nameof(Attempt.AgentRole), AgentRole.Implementer },
        { nameof(Attempt.AgentResponseContract), AgentResponseContract.ImplementationReview },
        { nameof(Attempt.AgentExpectedMessageType), CollaborationMessageType.ReviewApproval },
        { nameof(Attempt.AgentProtocolVersion), "2.0" },
        { nameof(Attempt.AgentPermissionProfile), AgentPermissionProfile.WorkspaceEditOnly },
        { nameof(Attempt.AgentAdapterContractVersion), "codex-implementation-review-v1" },
    };

    [Theory]
    [MemberData(nameof(WrongTupleMembers))]
    public void HasExactTuple_is_false_when_any_single_tuple_member_is_wrong(string propertyName, object? wrongValue)
    {
        var attempt = Claim();
        Assert.True(VerificationDiagnosisPolicy.HasExactTuple(attempt));

        typeof(Attempt).GetProperty(propertyName)!.SetValue(attempt, wrongValue);

        Assert.False(VerificationDiagnosisPolicy.HasExactTuple(attempt));
    }

    [Theory]
    [InlineData(nameof(Attempt.AgentProvider))]
    [InlineData(nameof(Attempt.AgentRole))]
    [InlineData(nameof(Attempt.AgentResponseContract))]
    [InlineData(nameof(Attempt.AgentExpectedMessageType))]
    [InlineData(nameof(Attempt.AgentProtocolVersion))]
    [InlineData(nameof(Attempt.AgentPermissionProfile))]
    [InlineData(nameof(Attempt.AgentAdapterContractVersion))]
    public void HasExactTuple_is_false_when_any_persisted_tuple_member_is_missing(string propertyName)
    {
        var attempt = Claim();
        typeof(Attempt).GetProperty(propertyName)!.SetValue(attempt, null);

        Assert.False(VerificationDiagnosisPolicy.HasExactTuple(attempt));
    }

    [Fact]
    public void HasExactTuple_is_false_for_an_ordinary_code_review_attempt()
    {
        var review = Attempt.ClaimAgentCodeReview(
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 1024, 2048, Now, 1);

        Assert.False(VerificationDiagnosisPolicy.HasExactTuple(review));
    }

    [Fact]
    public void The_diagnosis_findings_bounds_are_one_to_ten()
    {
        Assert.Equal(1, VerificationDiagnosisPolicy.MinimumFindings);
        Assert.Equal(10, VerificationDiagnosisPolicy.MaximumFindings);
    }

    [Fact]
    public void Policy_classifies_InputAlreadyDiagnosed_as_a_pre_invocation_outcome_that_never_carries_evidence()
    {
        Assert.True(AgentProcessEvidencePolicy.IsPreInvocationOutcome(AgentOutcome.InputAlreadyDiagnosed));
        Assert.False(AgentProcessEvidencePolicy.RequiresCleanExit(AgentOutcome.InputAlreadyDiagnosed));
        Assert.Null(AgentProcessEvidencePolicy.Evaluate(AgentOutcome.InputAlreadyDiagnosed, false, null));
        Assert.Equal(
            AgentProcessEvidenceViolation.PreInvocationOutcomeCannotCarryEvidence,
            AgentProcessEvidencePolicy.Evaluate(AgentOutcome.InputAlreadyDiagnosed, true, TestProcessEvidence.CleanExit));
        Assert.Equal(
            AgentProcessEvidenceViolation.PreInvocationOutcomeCannotCarryEvidence,
            AgentProcessEvidencePolicy.Evaluate(
                AgentOutcome.InputAlreadyDiagnosed, false,
                AgentProcessExecutionEvidence.Create(ProcessOutcome.Cancelled, null, TimeSpan.FromSeconds(1))));
    }

    [Fact]
    public void Policy_allows_VerificationEvidenceChanged_with_or_without_truthful_evidence_and_never_requires_a_clean_exit()
    {
        var outcome = AgentOutcome.VerificationEvidenceChanged;

        Assert.False(AgentProcessEvidencePolicy.IsPreInvocationOutcome(outcome));
        Assert.False(AgentProcessEvidencePolicy.RequiresCleanExit(outcome));
        Assert.Null(AgentProcessEvidencePolicy.Evaluate(outcome, false, null));
        Assert.Null(AgentProcessEvidencePolicy.Evaluate(outcome, true, null));
        Assert.Null(AgentProcessEvidencePolicy.Evaluate(outcome, true, TestProcessEvidence.CleanExit));
        Assert.Null(AgentProcessEvidencePolicy.Evaluate(
            outcome, true, AgentProcessExecutionEvidence.Create(ProcessOutcome.TimedOut, null, TimeSpan.FromSeconds(3))));
        Assert.Equal(
            AgentProcessEvidenceViolation.NotDispatched,
            AgentProcessEvidencePolicy.Evaluate(outcome, false, TestProcessEvidence.CleanExit));
    }

    [Theory]
    [InlineData(AgentOutcome.DiagnosisFindingsRecorded)]
    [InlineData(AgentOutcome.DiagnosisEscalated)]
    public void Policy_requires_a_clean_exit_for_diagnosis_findings_and_escalation(AgentOutcome outcome)
    {
        Assert.True(AgentProcessEvidencePolicy.RequiresCleanExit(outcome));
        Assert.False(AgentProcessEvidencePolicy.IsPreInvocationOutcome(outcome));
        Assert.Equal(AgentProcessEvidenceViolation.CleanExitRequired, AgentProcessEvidencePolicy.Evaluate(outcome, true, null));
        Assert.Equal(
            AgentProcessEvidenceViolation.CleanExitRequired,
            AgentProcessEvidencePolicy.Evaluate(
                outcome, true, AgentProcessExecutionEvidence.Create(ProcessOutcome.Exited, 3, TimeSpan.Zero)));
        Assert.Null(AgentProcessEvidencePolicy.Evaluate(outcome, true, TestProcessEvidence.CleanExit));
    }

    [Theory]
    [InlineData(AgentOutcome.DiagnosisFindingsRecorded)]
    [InlineData(AgentOutcome.DiagnosisEscalated)]
    public void CompleteAgent_completes_a_dispatched_diagnosis_with_findings_or_escalation_and_matching_fingerprint(AgentOutcome outcome)
    {
        var attempt = ClaimDispatched();

        attempt.CompleteAgent(outcome, Fingerprint, Now.AddSeconds(5), TestProcessEvidence.CleanExit);

        Assert.Equal(outcome, attempt.AgentOutcome);
        Assert.Equal(AttemptStatus.Completed, attempt.Status);
        Assert.Equal(Now.AddSeconds(5), attempt.CompletedAtUtc);
        Assert.Equal(TestProcessEvidence.CleanExit, attempt.GetAgentProcessExecutionEvidence());
    }

    [Theory]
    [InlineData(AgentOutcome.ReviewApproved)]
    [InlineData(AgentOutcome.ReviewChangesRequested)]
    [InlineData(AgentOutcome.Proposed)]
    [InlineData(AgentOutcome.Resolved)]
    public void CompleteAgent_rejects_every_other_contracts_read_only_success_outcome_for_a_diagnosis_without_mutation(AgentOutcome outcome)
    {
        var attempt = ClaimDispatched();

        Assert.Throws<InvalidOperationException>(
            () => attempt.CompleteAgent(outcome, Fingerprint, Now.AddSeconds(5), TestProcessEvidence.CleanExit));

        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Null(attempt.AgentOutcome);
        Assert.Null(attempt.CompletedAtUtc);
        Assert.Null(attempt.AgentProcessOutcome);
    }

    [Theory]
    [InlineData(AgentOutcome.ReviewApproved)]
    [InlineData(AgentOutcome.ReviewChangesRequested)]
    public void CompleteAgent_never_completes_a_diagnosis_as_an_approval_even_when_the_fingerprint_drifted(AgentOutcome outcome)
    {
        // A drifted fingerprint downgrades to SourceChanged; the attempt is a failure and never an approved review.
        var attempt = ClaimDispatched();

        attempt.CompleteAgent(outcome, "drifted", Now.AddSeconds(5), TestProcessEvidence.CleanExit);

        Assert.Equal(AgentOutcome.SourceChanged, attempt.AgentOutcome);
        Assert.Equal(AttemptStatus.Failed, attempt.Status);
    }

    [Theory]
    [InlineData(AgentOutcome.DiagnosisFindingsRecorded)]
    [InlineData(AgentOutcome.DiagnosisEscalated)]
    public void CompleteAgent_rejects_diagnosis_success_without_dispatch_or_without_fresh_fingerprint_or_clean_exit(AgentOutcome outcome)
    {
        var undispatched = Claim();
        Assert.Throws<InvalidOperationException>(() => undispatched.CompleteAgent(outcome, Fingerprint, Now));
        Assert.Equal(AttemptStatus.Running, undispatched.Status);

        var noFingerprint = ClaimDispatched();
        Assert.Throws<InvalidOperationException>(
            () => noFingerprint.CompleteAgent(outcome, null, Now.AddSeconds(5), TestProcessEvidence.CleanExit));
        Assert.Equal(AttemptStatus.Running, noFingerprint.Status);

        var noEvidence = ClaimDispatched();
        Assert.Throws<InvalidOperationException>(() => noEvidence.CompleteAgent(outcome, Fingerprint, Now.AddSeconds(5)));
        Assert.Equal(AttemptStatus.Running, noEvidence.Status);
        Assert.Null(noEvidence.AgentOutcome);
    }

    [Theory]
    [InlineData(AgentOutcome.DiagnosisFindingsRecorded)]
    [InlineData(AgentOutcome.DiagnosisEscalated)]
    public void CompleteAgent_turns_fingerprint_drift_into_SourceChanged_not_a_completed_diagnosis(AgentOutcome outcome)
    {
        var attempt = ClaimDispatched();

        attempt.CompleteAgent(outcome, "drifted", Now.AddSeconds(5), TestProcessEvidence.CleanExit);

        Assert.Equal(AgentOutcome.SourceChanged, attempt.AgentOutcome);
        Assert.Equal(AttemptStatus.Failed, attempt.Status);
    }

    [Fact]
    public void CompleteAgent_records_InputAlreadyDiagnosed_as_failed_with_no_evidence_even_when_undispatched()
    {
        var attempt = Claim();

        attempt.CompleteAgent(AgentOutcome.InputAlreadyDiagnosed, null, Now);

        Assert.Equal(AgentOutcome.InputAlreadyDiagnosed, attempt.AgentOutcome);
        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.Null(attempt.AgentProcessOutcome);
        Assert.Null(attempt.GetAgentProcessExecutionEvidence());
    }

    [Fact]
    public void CompleteAgent_rejects_evidence_for_InputAlreadyDiagnosed_even_when_dispatched()
    {
        var attempt = ClaimDispatched();

        Assert.Throws<InvalidOperationException>(
            () => attempt.CompleteAgent(AgentOutcome.InputAlreadyDiagnosed, null, Now.AddSeconds(5), TestProcessEvidence.CleanExit));

        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Null(attempt.AgentProcessOutcome);
    }

    [Fact]
    public void CompleteAgent_records_VerificationEvidenceChanged_as_failed_before_dispatch_without_evidence()
    {
        var attempt = Claim();

        attempt.CompleteAgent(AgentOutcome.VerificationEvidenceChanged, null, Now);

        Assert.Equal(AgentOutcome.VerificationEvidenceChanged, attempt.AgentOutcome);
        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.Null(attempt.GetAgentProcessExecutionEvidence());
    }

    [Fact]
    public void CompleteAgent_records_VerificationEvidenceChanged_after_dispatch_retaining_truthful_process_evidence()
    {
        var attempt = ClaimDispatched();

        attempt.CompleteAgent(AgentOutcome.VerificationEvidenceChanged, Fingerprint, Now.AddSeconds(5), TestProcessEvidence.CleanExit);

        Assert.Equal(AgentOutcome.VerificationEvidenceChanged, attempt.AgentOutcome);
        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.Equal(TestProcessEvidence.CleanExit, attempt.GetAgentProcessExecutionEvidence());
    }

    [Fact]
    public void A_diagnosis_attempt_can_never_complete_through_the_review_correction_transition()
    {
        var attempt = ClaimDispatched();

        Assert.Throws<InvalidOperationException>(
            () => attempt.CompleteReviewCorrection(AgentOutcome.DiagnosisFindingsRecorded, null, Now.AddSeconds(5), TestProcessEvidence.CleanExit));
        Assert.Throws<InvalidOperationException>(
            () => attempt.CompleteReviewCorrection(AgentOutcome.DiagnosisEscalated, null, Now.AddSeconds(5), TestProcessEvidence.CleanExit));

        Assert.Equal(AttemptStatus.Running, attempt.Status);
    }

    [Fact]
    public void Record_creates_a_diagnosis_correction_escalation_with_its_durable_identifiers()
    {
        var id = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var diagnosisId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        var escalation = DiagnosisCorrectionEscalation.Record(id, runId, diagnosisId, messageId, Now);

        Assert.Equal(id, escalation.Id);
        Assert.Equal(runId, escalation.RunId);
        Assert.Equal(diagnosisId, escalation.VerificationDiagnosisAttemptId);
        Assert.Equal(messageId, escalation.CollaborationMessageId);
        Assert.Equal(Now, escalation.CreatedAtUtc);
    }

    [Fact]
    public void Record_rejects_empty_identifiers_and_default_time()
    {
        var id = Guid.NewGuid();
        var run = Guid.NewGuid();
        var diagnosis = Guid.NewGuid();
        var message = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => DiagnosisCorrectionEscalation.Record(Guid.Empty, run, diagnosis, message, Now));
        Assert.Throws<ArgumentException>(() => DiagnosisCorrectionEscalation.Record(id, Guid.Empty, diagnosis, message, Now));
        Assert.Throws<ArgumentException>(() => DiagnosisCorrectionEscalation.Record(id, run, Guid.Empty, message, Now));
        Assert.Throws<ArgumentException>(() => DiagnosisCorrectionEscalation.Record(id, run, diagnosis, Guid.Empty, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => DiagnosisCorrectionEscalation.Record(id, run, diagnosis, message, default));
    }
}
