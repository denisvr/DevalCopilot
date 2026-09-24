using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

public sealed class AttemptTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);

    private static ProcessExecutionIntent CreateIntent() => new(
        ExecutablePath: @"C:\tools\build.exe",
        Arguments: ["--verify"],
        WorkingDirectory: @"C:\repos\devalcopilot",
        ApprovedRoot: @"C:\repos",
        Timeout: TimeSpan.FromMinutes(5),
        MaxBytesPerStream: 65536,
        MaxTotalCapturedBytes: 131072);

    private static Attempt ClaimAgentAttempt(
        string checkpointFingerprintSha256 = "fingerprint-1",
        TimeSpan? timeout = null,
        int maxBytesPerStream = 262144,
        int maxTotalCapturedBytes = 524288) => Attempt.ClaimAgent(
        Guid.NewGuid(),
        Guid.NewGuid(),
        attemptNumber: 1,
        gitWorkspaceId: Guid.NewGuid(),
        gitCheckpointId: Guid.NewGuid(),
        checkpointFingerprintSha256: checkpointFingerprintSha256,
        contextManifestArtifactId: Guid.NewGuid(),
        timeout: timeout ?? TimeSpan.FromMinutes(10),
        maxBytesPerStream: maxBytesPerStream,
        maxTotalCapturedBytes: maxTotalCapturedBytes,
        claimedAtUtc: BaseTime);

    private static Attempt ClaimReviewCorrectionAttempt() => Attempt.ClaimAgentReviewCorrection(
        Guid.NewGuid(),
        Guid.NewGuid(),
        attemptNumber: 1,
        gitWorkspaceId: Guid.NewGuid(),
        gitCheckpointId: Guid.NewGuid(),
        checkpointFingerprintSha256: "fingerprint-1",
        contextManifestArtifactId: Guid.NewGuid(),
        timeout: TimeSpan.FromMinutes(20),
        maxBytesPerStream: 262144,
        maxTotalCapturedBytes: 524288,
        claimedAtUtc: BaseTime);

    [Fact]
    public void Claim_creates_a_simulated_attempt()
    {
        var attempt = Attempt.Claim(Guid.NewGuid(), Guid.NewGuid(), attemptNumber: 1, BaseTime);

        Assert.Equal(AttemptKind.Simulated, attempt.Kind);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Empty(attempt.ProcessArguments);
        Assert.Null(attempt.ProcessExecutablePath);
    }

    [Fact]
    public void Claim_throws_for_a_non_positive_attempt_number()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Attempt.Claim(Guid.NewGuid(), Guid.NewGuid(), 0, BaseTime));
    }

    [Fact]
    public void ClaimProcess_creates_a_process_attempt_with_its_durable_intent_persisted()
    {
        var intent = CreateIntent();

        var attempt = Attempt.ClaimProcess(Guid.NewGuid(), Guid.NewGuid(), attemptNumber: 1, intent, BaseTime);

        Assert.Equal(AttemptKind.Process, attempt.Kind);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Equal(intent.ExecutablePath, attempt.ProcessExecutablePath);
        Assert.Equal(intent.Arguments, attempt.ProcessArguments);
        Assert.Equal(intent.WorkingDirectory, attempt.ProcessWorkingDirectory);
        Assert.Equal(intent.ApprovedRoot, attempt.ProcessApprovedRoot);
        Assert.Equal(intent.Timeout, attempt.ProcessTimeout);
        Assert.Equal(intent.MaxBytesPerStream, attempt.ProcessMaxBytesPerStream);
        Assert.Equal(intent.MaxTotalCapturedBytes, attempt.ProcessMaxTotalCapturedBytes);
        Assert.Null(attempt.ProcessOutcome);
        Assert.Null(attempt.ProcessExitCode);
        Assert.Null(attempt.ProcessDispatchedAtUtc);
    }

    [Fact]
    public void ClaimProcess_defensively_copies_the_argument_list_so_a_later_caller_mutation_cannot_alter_it()
    {
        var mutableArguments = new List<string> { "--verify" };
        var intent = CreateIntent() with { Arguments = mutableArguments };

        var attempt = Attempt.ClaimProcess(Guid.NewGuid(), Guid.NewGuid(), 1, intent, BaseTime);

        mutableArguments.Add("--tampered");
        mutableArguments[0] = "--also-tampered";

        Assert.Equal(["--verify"], attempt.ProcessArguments);
    }

    [Fact]
    public void ClaimProcess_throws_for_a_non_positive_attempt_number()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Attempt.ClaimProcess(Guid.NewGuid(), Guid.NewGuid(), 0, CreateIntent(), BaseTime));
    }

    [Fact]
    public void Complete_transitions_a_running_attempt_to_completed()
    {
        var attempt = Attempt.Claim(Guid.NewGuid(), Guid.NewGuid(), 1, BaseTime);

        attempt.Complete(BaseTime.AddSeconds(1));

        Assert.Equal(AttemptStatus.Completed, attempt.Status);
        Assert.Equal(BaseTime.AddSeconds(1), attempt.CompletedAtUtc);
    }

    [Fact]
    public void Complete_throws_when_attempt_is_not_running()
    {
        var attempt = Attempt.Claim(Guid.NewGuid(), Guid.NewGuid(), 1, BaseTime);
        attempt.Complete(BaseTime.AddSeconds(1));

        Assert.Throws<InvalidOperationException>(() => attempt.Complete(BaseTime.AddSeconds(2)));
    }

    [Fact]
    public void CompleteReviewCorrection_records_a_successful_correction_with_its_result_checkpoint()
    {
        var attempt = ClaimReviewCorrectionAttempt();
        var resultCheckpointId = Guid.NewGuid();
        attempt.MarkAgentDispatched(BaseTime.AddSeconds(1));

        attempt.CompleteReviewCorrection(AgentOutcome.CorrectionApplied, resultCheckpointId, BaseTime.AddSeconds(2), processEvidence: TestProcessEvidence.CleanExit);

        Assert.Equal(AttemptStatus.Completed, attempt.Status);
        Assert.Equal(AgentOutcome.CorrectionApplied, attempt.AgentOutcome);
        Assert.Equal(resultCheckpointId, attempt.AgentResultGitCheckpointId);
        Assert.Equal(BaseTime.AddSeconds(2), attempt.CompletedAtUtc);
    }

    [Theory]
    [InlineData(AgentOutcome.CorrectionNoChangesProduced)]
    [InlineData(AgentOutcome.InputAlreadyCorrected)]
    [InlineData(AgentOutcome.CorrectionHeadChanged)]
    [InlineData(AgentOutcome.CheckpointEvidenceUnavailable)]
    [InlineData(AgentOutcome.ProviderInvocationFailed)]
    [InlineData(AgentOutcome.InvalidStructuredOutput)]
    public void CompleteReviewCorrection_records_non_successful_outcomes_without_a_result_checkpoint(AgentOutcome outcome)
    {
        var attempt = ClaimReviewCorrectionAttempt();
        var requiresCleanExit = AgentProcessEvidencePolicy.RequiresCleanExit(outcome);
        if (requiresCleanExit)
        {
            attempt.MarkAgentDispatched(BaseTime);
        }

        attempt.CompleteReviewCorrection(
            outcome, resultGitCheckpointId: null, BaseTime.AddSeconds(1), requiresCleanExit ? TestProcessEvidence.CleanExit : null);

        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.Equal(outcome, attempt.AgentOutcome);
        Assert.Null(attempt.AgentResultGitCheckpointId);
    }

    [Fact]
    public void CompleteReviewCorrection_rejects_success_before_dispatch()
    {
        var attempt = ClaimReviewCorrectionAttempt();

        Assert.Throws<InvalidOperationException>(() => attempt.CompleteReviewCorrection(
            AgentOutcome.CorrectionApplied, Guid.NewGuid(), BaseTime.AddSeconds(1), processEvidence: TestProcessEvidence.CleanExit));
    }

    [Theory]
    [InlineData(AgentOutcome.Proposed)]
    [InlineData(AgentOutcome.Accepted)]
    [InlineData(AgentOutcome.Challenged)]
    [InlineData(AgentOutcome.Resolved)]
    [InlineData(AgentOutcome.Implemented)]
    [InlineData(AgentOutcome.ReviewApproved)]
    [InlineData(AgentOutcome.ReviewChangesRequested)]
    public void CompleteReviewCorrection_rejects_an_outcome_from_another_agent_contract(AgentOutcome outcome)
    {
        var attempt = ClaimReviewCorrectionAttempt();

        Assert.Throws<InvalidOperationException>(() => attempt.CompleteReviewCorrection(outcome, null, BaseTime.AddSeconds(1)));
    }

    [Fact]
    public void Fail_transitions_a_running_attempt_to_failed_without_recording_a_process_outcome()
    {
        var attempt = Attempt.ClaimProcess(Guid.NewGuid(), Guid.NewGuid(), 1, CreateIntent(), BaseTime);

        attempt.Fail(BaseTime.AddSeconds(1));

        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.Null(attempt.ProcessOutcome);
        Assert.Null(attempt.ProcessExitCode);
    }

    [Fact]
    public void Fail_throws_when_attempt_is_not_running()
    {
        var attempt = Attempt.Claim(Guid.NewGuid(), Guid.NewGuid(), 1, BaseTime);
        attempt.Fail(BaseTime.AddSeconds(1));

        Assert.Throws<InvalidOperationException>(() => attempt.Fail(BaseTime.AddSeconds(2)));
    }

    [Theory]
    [InlineData(0, AttemptStatus.Completed)]
    [InlineData(1, AttemptStatus.Failed)]
    [InlineData(127, AttemptStatus.Failed)]
    public void CompleteProcess_derives_status_from_an_exited_outcome_and_its_exit_code(int exitCode, AttemptStatus expectedStatus)
    {
        var attempt = Attempt.ClaimProcess(Guid.NewGuid(), Guid.NewGuid(), 1, CreateIntent(), BaseTime);

        attempt.CompleteProcess(ProcessOutcome.Exited, exitCode, BaseTime.AddSeconds(1));

        Assert.Equal(expectedStatus, attempt.Status);
        Assert.Equal(ProcessOutcome.Exited, attempt.ProcessOutcome);
        Assert.Equal(exitCode, attempt.ProcessExitCode);
    }

    [Theory]
    [InlineData(ProcessOutcome.TimedOut)]
    [InlineData(ProcessOutcome.Cancelled)]
    public void CompleteProcess_fails_for_a_timed_out_or_cancelled_outcome_with_no_exit_code(ProcessOutcome outcome)
    {
        var attempt = Attempt.ClaimProcess(Guid.NewGuid(), Guid.NewGuid(), 1, CreateIntent(), BaseTime);

        attempt.CompleteProcess(outcome, exitCode: null, BaseTime.AddSeconds(1));

        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.Equal(outcome, attempt.ProcessOutcome);
        Assert.Null(attempt.ProcessExitCode);
    }

    [Fact]
    public void CompleteProcess_throws_when_exited_is_reported_without_an_exit_code()
    {
        var attempt = Attempt.ClaimProcess(Guid.NewGuid(), Guid.NewGuid(), 1, CreateIntent(), BaseTime);

        Assert.Throws<ArgumentException>(
            () => attempt.CompleteProcess(ProcessOutcome.Exited, exitCode: null, BaseTime.AddSeconds(1)));
    }

    [Theory]
    [InlineData(ProcessOutcome.TimedOut)]
    [InlineData(ProcessOutcome.Cancelled)]
    public void CompleteProcess_throws_when_a_non_exited_outcome_carries_an_exit_code(ProcessOutcome outcome)
    {
        var attempt = Attempt.ClaimProcess(Guid.NewGuid(), Guid.NewGuid(), 1, CreateIntent(), BaseTime);

        Assert.Throws<ArgumentException>(() => attempt.CompleteProcess(outcome, exitCode: 0, BaseTime.AddSeconds(1)));
    }

    [Fact]
    public void CompleteProcess_throws_for_a_simulated_attempt()
    {
        var attempt = Attempt.Claim(Guid.NewGuid(), Guid.NewGuid(), 1, BaseTime);

        Assert.Throws<InvalidOperationException>(
            () => attempt.CompleteProcess(ProcessOutcome.Exited, exitCode: 0, BaseTime.AddSeconds(1)));
    }

    [Fact]
    public void CompleteProcess_throws_when_attempt_is_not_running()
    {
        var attempt = Attempt.ClaimProcess(Guid.NewGuid(), Guid.NewGuid(), 1, CreateIntent(), BaseTime);
        attempt.CompleteProcess(ProcessOutcome.Exited, 0, BaseTime.AddSeconds(1));

        Assert.Throws<InvalidOperationException>(
            () => attempt.CompleteProcess(ProcessOutcome.Exited, 0, BaseTime.AddSeconds(2)));
    }

    [Fact]
    public void Interrupt_transitions_a_running_attempt_to_interrupted()
    {
        var attempt = Attempt.ClaimProcess(Guid.NewGuid(), Guid.NewGuid(), 1, CreateIntent(), BaseTime);

        attempt.Interrupt(BaseTime.AddSeconds(1));

        Assert.Equal(AttemptStatus.Interrupted, attempt.Status);
        Assert.Equal(BaseTime.AddSeconds(1), attempt.CompletedAtUtc);
    }

    [Fact]
    public void Interrupt_throws_when_attempt_is_not_running()
    {
        var attempt = Attempt.ClaimProcess(Guid.NewGuid(), Guid.NewGuid(), 1, CreateIntent(), BaseTime);
        attempt.Interrupt(BaseTime.AddSeconds(1));

        Assert.Throws<InvalidOperationException>(() => attempt.Interrupt(BaseTime.AddSeconds(2)));
    }

    [Fact]
    public void MarkProcessDispatched_records_the_execution_start_claim_once()
    {
        var attempt = Attempt.ClaimProcess(Guid.NewGuid(), Guid.NewGuid(), 1, CreateIntent(), BaseTime);

        attempt.MarkProcessDispatched(BaseTime.AddSeconds(1));

        Assert.Equal(BaseTime.AddSeconds(1), attempt.ProcessDispatchedAtUtc);
        // Still Running and not yet terminal — dispatched is a sub-state, not a status change.
        Assert.Equal(AttemptStatus.Running, attempt.Status);
    }

    [Fact]
    public void MarkProcessDispatched_throws_when_already_dispatched()
    {
        var attempt = Attempt.ClaimProcess(Guid.NewGuid(), Guid.NewGuid(), 1, CreateIntent(), BaseTime);
        attempt.MarkProcessDispatched(BaseTime.AddSeconds(1));

        Assert.Throws<InvalidOperationException>(() => attempt.MarkProcessDispatched(BaseTime.AddSeconds(2)));
    }

    [Fact]
    public void MarkProcessDispatched_throws_for_a_simulated_attempt()
    {
        var attempt = Attempt.Claim(Guid.NewGuid(), Guid.NewGuid(), 1, BaseTime);

        Assert.Throws<InvalidOperationException>(() => attempt.MarkProcessDispatched(BaseTime.AddSeconds(1)));
    }

    [Fact]
    public void MarkProcessDispatched_throws_when_attempt_is_not_running()
    {
        var attempt = Attempt.ClaimProcess(Guid.NewGuid(), Guid.NewGuid(), 1, CreateIntent(), BaseTime);
        attempt.Fail(BaseTime.AddSeconds(1));

        Assert.Throws<InvalidOperationException>(() => attempt.MarkProcessDispatched(BaseTime.AddSeconds(2)));
    }

    [Fact]
    public void ClaimAgent_creates_an_agent_attempt_with_its_durable_intent_persisted()
    {
        var workspaceId = Guid.NewGuid();
        var checkpointId = Guid.NewGuid();
        var manifestArtifactId = Guid.NewGuid();
        var timeout = TimeSpan.FromMinutes(10);

        var attempt = Attempt.ClaimAgent(
            Guid.NewGuid(), Guid.NewGuid(), attemptNumber: 1, workspaceId, checkpointId,
            "fingerprint-1", manifestArtifactId, timeout, 262144, 524288, BaseTime);

        Assert.Equal(AttemptKind.Agent, attempt.Kind);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Equal(AgentProvider.Codex, attempt.AgentProvider);
        Assert.Equal(AgentRole.Planner, attempt.AgentRole);
        Assert.Equal(CollaborationMessage.ProtocolVersionOne, attempt.AgentProtocolVersion);
        Assert.Equal(CollaborationMessageType.Proposal, attempt.AgentExpectedMessageType);
        Assert.Equal(workspaceId, attempt.AgentGitWorkspaceId);
        Assert.Equal(checkpointId, attempt.AgentGitCheckpointId);
        Assert.Equal("fingerprint-1", attempt.AgentCheckpointFingerprintSha256);
        Assert.Equal(manifestArtifactId, attempt.AgentContextManifestArtifactId);
        Assert.Equal(timeout, attempt.AgentTimeout);
        Assert.Equal(262144, attempt.AgentMaxBytesPerStream);
        Assert.Equal(524288, attempt.AgentMaxTotalCapturedBytes);
        Assert.Null(attempt.AgentDispatchedAtUtc);
        Assert.Null(attempt.AgentOutcome);
        Assert.Null(attempt.AgentProviderSessionId);
        // A Process-shaped field is never populated by an Agent claim.
        Assert.Null(attempt.ProcessExecutablePath);
    }

    [Fact]
    public void ClaimAgent_throws_for_a_non_positive_attempt_number()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Attempt.ClaimAgent(
            Guid.NewGuid(), Guid.NewGuid(), 0, Guid.NewGuid(), Guid.NewGuid(),
            "fingerprint-1", Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, BaseTime));
    }

    [Fact]
    public void ClaimAgent_throws_for_an_empty_git_workspace_id()
    {
        Assert.Throws<ArgumentException>(() => Attempt.ClaimAgent(
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.Empty, Guid.NewGuid(),
            "fingerprint-1", Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, BaseTime));
    }

    [Fact]
    public void ClaimAgent_throws_for_an_empty_git_checkpoint_id()
    {
        Assert.Throws<ArgumentException>(() => Attempt.ClaimAgent(
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.Empty,
            "fingerprint-1", Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, BaseTime));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ClaimAgent_throws_for_a_missing_checkpoint_fingerprint(string? fingerprint)
    {
        Assert.ThrowsAny<ArgumentException>(() => Attempt.ClaimAgent(
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(),
            fingerprint!, Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, BaseTime));
    }

    [Fact]
    public void ClaimAgent_throws_for_an_empty_context_manifest_artifact_id()
    {
        Assert.Throws<ArgumentException>(() => Attempt.ClaimAgent(
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(),
            "fingerprint-1", Guid.Empty, TimeSpan.FromMinutes(10), 262144, 524288, BaseTime));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ClaimAgent_throws_for_a_non_positive_timeout(int timeoutSeconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Attempt.ClaimAgent(
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(),
            "fingerprint-1", Guid.NewGuid(), TimeSpan.FromSeconds(timeoutSeconds), 262144, 524288, BaseTime));
    }

    [Fact]
    public void ClaimAgent_throws_for_a_negative_max_bytes_per_stream()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Attempt.ClaimAgent(
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(),
            "fingerprint-1", Guid.NewGuid(), TimeSpan.FromMinutes(10), -1, 524288, BaseTime));
    }

    [Fact]
    public void ClaimAgent_throws_for_a_negative_max_total_captured_bytes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Attempt.ClaimAgent(
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(),
            "fingerprint-1", Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, -1, BaseTime));
    }

    [Fact]
    public void MarkAgentDispatched_records_the_execution_start_claim_once()
    {
        var attempt = ClaimAgentAttempt();

        attempt.MarkAgentDispatched(BaseTime.AddSeconds(1));

        Assert.Equal(BaseTime.AddSeconds(1), attempt.AgentDispatchedAtUtc);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
    }

    [Fact]
    public void MarkAgentDispatched_throws_when_already_dispatched()
    {
        var attempt = ClaimAgentAttempt();
        attempt.MarkAgentDispatched(BaseTime.AddSeconds(1));

        Assert.Throws<InvalidOperationException>(() => attempt.MarkAgentDispatched(BaseTime.AddSeconds(2)));
    }

    [Fact]
    public void MarkAgentDispatched_throws_for_a_process_attempt()
    {
        var attempt = Attempt.ClaimProcess(Guid.NewGuid(), Guid.NewGuid(), 1, CreateIntent(), BaseTime);

        Assert.Throws<InvalidOperationException>(() => attempt.MarkAgentDispatched(BaseTime.AddSeconds(1)));
    }

    [Fact]
    public void MarkAgentDispatched_throws_when_attempt_is_not_running()
    {
        var attempt = ClaimAgentAttempt();
        attempt.CompleteAgent(AgentOutcome.ProviderInvocationFailed, completionFingerprintSha256: null, BaseTime.AddSeconds(1));

        Assert.Throws<InvalidOperationException>(() => attempt.MarkAgentDispatched(BaseTime.AddSeconds(2)));
    }

    [Fact]
    public void RecordAgentProviderSessionId_records_the_session_id_once()
    {
        var attempt = ClaimAgentAttempt();
        attempt.MarkAgentDispatched(BaseTime.AddSeconds(1));

        attempt.RecordAgentProviderSessionId("session-abc");

        Assert.Equal("session-abc", attempt.AgentProviderSessionId);
    }

    [Fact]
    public void RecordAgentProviderSessionId_throws_when_already_recorded()
    {
        var attempt = ClaimAgentAttempt();
        attempt.RecordAgentProviderSessionId("session-abc");

        Assert.Throws<InvalidOperationException>(() => attempt.RecordAgentProviderSessionId("session-def"));
    }

    [Fact]
    public void RecordAgentProviderSessionId_throws_for_a_non_agent_attempt()
    {
        var attempt = Attempt.ClaimProcess(Guid.NewGuid(), Guid.NewGuid(), 1, CreateIntent(), BaseTime);

        Assert.Throws<InvalidOperationException>(() => attempt.RecordAgentProviderSessionId("session-abc"));
    }

    [Fact]
    public void RecordAgentProviderSessionId_throws_for_a_terminal_attempt()
    {
        var attempt = ClaimAgentAttempt();
        attempt.MarkAgentDispatched(BaseTime.AddSeconds(1));
        attempt.CompleteAgent(AgentOutcome.Proposed, completionFingerprintSha256: "fingerprint-1", BaseTime.AddSeconds(2), processEvidence: TestProcessEvidence.CleanExit);

        Assert.Throws<InvalidOperationException>(() => attempt.RecordAgentProviderSessionId("session-abc"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RecordAgentProviderSessionId_throws_for_a_null_or_whitespace_value(string? sessionId)
    {
        var attempt = ClaimAgentAttempt();

        Assert.ThrowsAny<ArgumentException>(() => attempt.RecordAgentProviderSessionId(sessionId!));
    }

    [Fact]
    public void CompleteAgent_completes_with_the_proposed_outcome_when_the_fingerprint_still_matches()
    {
        var attempt = ClaimAgentAttempt(checkpointFingerprintSha256: "fingerprint-1");
        attempt.MarkAgentDispatched(BaseTime.AddSeconds(1));

        attempt.CompleteAgent(AgentOutcome.Proposed, completionFingerprintSha256: "fingerprint-1", BaseTime.AddSeconds(2), processEvidence: TestProcessEvidence.CleanExit);

        Assert.Equal(AttemptStatus.Completed, attempt.Status);
        Assert.Equal(AgentOutcome.Proposed, attempt.AgentOutcome);
        Assert.Equal(BaseTime.AddSeconds(2), attempt.CompletedAtUtc);
    }

    [Theory]
    [InlineData(AgentOutcome.SourceChanged)]
    [InlineData(AgentOutcome.InvalidStructuredOutput)]
    [InlineData(AgentOutcome.ProviderInvocationFailed)]
    public void CompleteAgent_fails_for_every_non_proposed_outcome(AgentOutcome outcome)
    {
        var attempt = ClaimAgentAttempt();
        attempt.MarkAgentDispatched(BaseTime.AddSeconds(1));

        attempt.CompleteAgent(
            outcome,
            completionFingerprintSha256: "fingerprint-1",
            BaseTime.AddSeconds(2),
            AgentProcessEvidencePolicy.RequiresCleanExit(outcome) ? TestProcessEvidence.CleanExit : null);

        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.Equal(outcome, attempt.AgentOutcome);
    }

    [Fact]
    public void CompleteAgent_overrides_to_source_changed_when_the_completion_fingerprint_no_longer_matches()
    {
        var attempt = ClaimAgentAttempt(checkpointFingerprintSha256: "fingerprint-1");
        attempt.MarkAgentDispatched(BaseTime.AddSeconds(1));

        // The caller believes the outcome is a successful Proposal, but fresh Git evidence
        // disagrees with what this attempt committed to at claim time — the override rule
        // must win regardless of what the caller passed as `outcome`.
        attempt.CompleteAgent(AgentOutcome.Proposed, completionFingerprintSha256: "fingerprint-2", BaseTime.AddSeconds(2), processEvidence: TestProcessEvidence.CleanExit);

        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.Equal(AgentOutcome.SourceChanged, attempt.AgentOutcome);
    }

    [Fact]
    public void CompleteAgent_completes_without_a_completion_fingerprint_when_source_drift_was_already_detected_pre_dispatch()
    {
        var attempt = ClaimAgentAttempt();
        // Never dispatched: the source-drift check that runs immediately before dispatch
        // already found the drift, so there is no fresh completion evidence to compare.

        attempt.CompleteAgent(AgentOutcome.SourceChanged, completionFingerprintSha256: null, BaseTime.AddSeconds(1));

        Assert.Null(attempt.AgentDispatchedAtUtc);
        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.Equal(AgentOutcome.SourceChanged, attempt.AgentOutcome);
    }

    [Fact]
    public void CompleteAgent_throws_for_a_non_agent_attempt()
    {
        var attempt = Attempt.ClaimProcess(Guid.NewGuid(), Guid.NewGuid(), 1, CreateIntent(), BaseTime);

        Assert.Throws<InvalidOperationException>(
            () => attempt.CompleteAgent(AgentOutcome.Proposed, completionFingerprintSha256: null, BaseTime.AddSeconds(1), processEvidence: TestProcessEvidence.CleanExit));
    }

    [Fact]
    public void CompleteAgent_throws_when_attempt_is_not_running()
    {
        var attempt = ClaimAgentAttempt();
        attempt.MarkAgentDispatched(BaseTime.AddSeconds(1));
        attempt.CompleteAgent(AgentOutcome.Proposed, completionFingerprintSha256: "fingerprint-1", BaseTime.AddSeconds(2), processEvidence: TestProcessEvidence.CleanExit);

        Assert.Throws<InvalidOperationException>(
            () => attempt.CompleteAgent(AgentOutcome.Proposed, completionFingerprintSha256: "fingerprint-1", BaseTime.AddSeconds(3), processEvidence: TestProcessEvidence.CleanExit));
    }

    [Fact]
    public void CompleteAgent_throws_for_an_undefined_outcome()
    {
        var attempt = ClaimAgentAttempt();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => attempt.CompleteAgent((AgentOutcome)999, completionFingerprintSha256: null, BaseTime.AddSeconds(1)));
    }

    // Domain-level backstop (never the only line of defense — the Application boundary that
    // records a provider result rejects this before ever reaching CompleteAgent): a Proposal is
    // never observable for an attempt that was never actually dispatched to the provider.
    [Fact]
    public void CompleteAgent_throws_for_a_proposed_outcome_on_a_never_dispatched_attempt()
    {
        var attempt = ClaimAgentAttempt(checkpointFingerprintSha256: "fingerprint-1");

        Assert.Throws<InvalidOperationException>(
            () => attempt.CompleteAgent(AgentOutcome.Proposed, completionFingerprintSha256: "fingerprint-1", BaseTime.AddSeconds(1), processEvidence: TestProcessEvidence.CleanExit));
    }

    // Domain-level backstop: a Proposal is never observable without fresh completion evidence
    // confirming the checkpoint it claims to be about — even for an attempt that was dispatched.
    [Fact]
    public void CompleteAgent_throws_for_a_proposed_outcome_without_a_completion_fingerprint()
    {
        var attempt = ClaimAgentAttempt();
        attempt.MarkAgentDispatched(BaseTime.AddSeconds(1));

        Assert.Throws<InvalidOperationException>(
            () => attempt.CompleteAgent(AgentOutcome.Proposed, completionFingerprintSha256: null, BaseTime.AddSeconds(2), processEvidence: TestProcessEvidence.CleanExit));
    }

    [Fact]
    public void Interrupt_transitions_a_running_agent_attempt_to_interrupted_whether_or_not_it_was_dispatched()
    {
        var attempt = ClaimAgentAttempt();

        attempt.Interrupt(BaseTime.AddSeconds(1));

        Assert.Equal(AttemptStatus.Interrupted, attempt.Status);
        Assert.Equal(BaseTime.AddSeconds(1), attempt.CompletedAtUtc);
        Assert.Null(attempt.AgentOutcome);
    }

    private static Attempt ClaimAgentCriticalReviewAttempt(
        string checkpointFingerprintSha256 = "fingerprint-1",
        TimeSpan? timeout = null,
        int maxBytesPerStream = 262144,
        int maxTotalCapturedBytes = 524288) => Attempt.ClaimAgentCriticalReview(
        Guid.NewGuid(),
        Guid.NewGuid(),
        attemptNumber: 1,
        gitWorkspaceId: Guid.NewGuid(),
        gitCheckpointId: Guid.NewGuid(),
        checkpointFingerprintSha256: checkpointFingerprintSha256,
        contextManifestArtifactId: Guid.NewGuid(),
        timeout: timeout ?? TimeSpan.FromMinutes(10),
        maxBytesPerStream: maxBytesPerStream,
        maxTotalCapturedBytes: maxTotalCapturedBytes,
        claimedAtUtc: BaseTime);

    private static Attempt ClaimAgentChallengeResolutionAttempt(
        string checkpointFingerprintSha256 = "fingerprint-1",
        TimeSpan? timeout = null,
        int maxBytesPerStream = 262144,
        int maxTotalCapturedBytes = 524288) => Attempt.ClaimAgentChallengeResolution(
        Guid.NewGuid(),
        Guid.NewGuid(),
        attemptNumber: 1,
        gitWorkspaceId: Guid.NewGuid(),
        gitCheckpointId: Guid.NewGuid(),
        checkpointFingerprintSha256: checkpointFingerprintSha256,
        contextManifestArtifactId: Guid.NewGuid(),
        timeout: timeout ?? TimeSpan.FromMinutes(10),
        maxBytesPerStream: maxBytesPerStream,
        maxTotalCapturedBytes: maxTotalCapturedBytes,
        claimedAtUtc: BaseTime);

    [Fact]
    public void ClaimAgentCriticalReview_creates_a_critical_review_attempt_with_its_durable_intent_persisted()
    {
        var workspaceId = Guid.NewGuid();
        var checkpointId = Guid.NewGuid();
        var manifestArtifactId = Guid.NewGuid();
        var timeout = TimeSpan.FromMinutes(10);

        var attempt = Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), Guid.NewGuid(), attemptNumber: 1, workspaceId, checkpointId,
            "fingerprint-1", manifestArtifactId, timeout, 262144, 524288, BaseTime);

        Assert.Equal(AttemptKind.Agent, attempt.Kind);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Equal(AgentProvider.ClaudeCode, attempt.AgentProvider);
        Assert.Equal(AgentRole.CriticalReviewer, attempt.AgentRole);
        Assert.Equal(AgentResponseContract.CriticalReview, attempt.AgentResponseContract);
        // Documents the explicit design note on ClaimAgentCriticalReview: the claimed intent is
        // still, in protocol terms, "expect to produce one Proposal-replying message" — the real
        // Accepted/Challenged union is represented by AgentResponseContract above, never by this
        // field being anything other than Proposal for a critical-review attempt.
        Assert.Equal(CollaborationMessageType.Proposal, attempt.AgentExpectedMessageType);
        Assert.Equal(CollaborationMessage.ProtocolVersionOne, attempt.AgentProtocolVersion);
        Assert.Equal(workspaceId, attempt.AgentGitWorkspaceId);
        Assert.Equal(checkpointId, attempt.AgentGitCheckpointId);
        Assert.Equal("fingerprint-1", attempt.AgentCheckpointFingerprintSha256);
        Assert.Equal(manifestArtifactId, attempt.AgentContextManifestArtifactId);
        Assert.Equal(timeout, attempt.AgentTimeout);
        Assert.Equal(262144, attempt.AgentMaxBytesPerStream);
        Assert.Equal(524288, attempt.AgentMaxTotalCapturedBytes);
        Assert.Null(attempt.AgentDispatchedAtUtc);
        Assert.Null(attempt.AgentOutcome);
        Assert.Null(attempt.AgentProviderSessionId);
        // A Process-shaped field is never populated by an Agent claim.
        Assert.Null(attempt.ProcessExecutablePath);
    }

    [Fact]
    public void ClaimAgentCriticalReview_throws_for_a_non_positive_attempt_number()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), Guid.NewGuid(), 0, Guid.NewGuid(), Guid.NewGuid(),
            "fingerprint-1", Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, BaseTime));
    }

    [Fact]
    public void ClaimAgentCriticalReview_throws_for_an_empty_git_workspace_id()
    {
        Assert.Throws<ArgumentException>(() => Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.Empty, Guid.NewGuid(),
            "fingerprint-1", Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, BaseTime));
    }

    [Fact]
    public void ClaimAgentCriticalReview_throws_for_an_empty_git_checkpoint_id()
    {
        Assert.Throws<ArgumentException>(() => Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.Empty,
            "fingerprint-1", Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, BaseTime));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ClaimAgentCriticalReview_throws_for_a_missing_checkpoint_fingerprint(string? fingerprint)
    {
        Assert.ThrowsAny<ArgumentException>(() => Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(),
            fingerprint!, Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, BaseTime));
    }

    [Fact]
    public void ClaimAgentCriticalReview_throws_for_an_empty_context_manifest_artifact_id()
    {
        Assert.Throws<ArgumentException>(() => Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(),
            "fingerprint-1", Guid.Empty, TimeSpan.FromMinutes(10), 262144, 524288, BaseTime));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ClaimAgentCriticalReview_throws_for_a_non_positive_timeout(int timeoutSeconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(),
            "fingerprint-1", Guid.NewGuid(), TimeSpan.FromSeconds(timeoutSeconds), 262144, 524288, BaseTime));
    }

    [Fact]
    public void ClaimAgentCriticalReview_throws_for_a_negative_max_bytes_per_stream()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(),
            "fingerprint-1", Guid.NewGuid(), TimeSpan.FromMinutes(10), -1, 524288, BaseTime));
    }

    [Fact]
    public void ClaimAgentCriticalReview_throws_for_a_negative_max_total_captured_bytes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(),
            "fingerprint-1", Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, -1, BaseTime));
    }

    [Fact]
    public void ClaimAgentChallengeResolution_creates_a_challenge_resolution_attempt_with_its_durable_intent_persisted()
    {
        var workspaceId = Guid.NewGuid();
        var checkpointId = Guid.NewGuid();
        var manifestArtifactId = Guid.NewGuid();
        var timeout = TimeSpan.FromMinutes(10);

        var attempt = Attempt.ClaimAgentChallengeResolution(
            Guid.NewGuid(), Guid.NewGuid(), attemptNumber: 1, workspaceId, checkpointId,
            "fingerprint-1", manifestArtifactId, timeout, 262144, 524288, BaseTime);

        Assert.Equal(AttemptKind.Agent, attempt.Kind);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Equal(AgentProvider.Codex, attempt.AgentProvider);
        Assert.Equal(AgentRole.Resolver, attempt.AgentRole);
        Assert.Equal(AgentResponseContract.ChallengeResolution, attempt.AgentResponseContract);
        Assert.Equal(CollaborationMessageType.Proposal, attempt.AgentExpectedMessageType);
        Assert.Equal(CollaborationMessage.ProtocolVersionOne, attempt.AgentProtocolVersion);
        Assert.Equal(workspaceId, attempt.AgentGitWorkspaceId);
        Assert.Equal(checkpointId, attempt.AgentGitCheckpointId);
        Assert.Equal("fingerprint-1", attempt.AgentCheckpointFingerprintSha256);
        Assert.Equal(manifestArtifactId, attempt.AgentContextManifestArtifactId);
        Assert.Equal(timeout, attempt.AgentTimeout);
        Assert.Equal(262144, attempt.AgentMaxBytesPerStream);
        Assert.Equal(524288, attempt.AgentMaxTotalCapturedBytes);
        Assert.Null(attempt.AgentDispatchedAtUtc);
        Assert.Null(attempt.AgentOutcome);
        Assert.Null(attempt.AgentProviderSessionId);
        Assert.Null(attempt.ProcessExecutablePath);
    }

    [Fact]
    public void ClaimAgentChallengeResolution_throws_for_a_non_positive_attempt_number()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Attempt.ClaimAgentChallengeResolution(
            Guid.NewGuid(), Guid.NewGuid(), 0, Guid.NewGuid(), Guid.NewGuid(),
            "fingerprint-1", Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, BaseTime));
    }

    [Fact]
    public void ClaimAgentChallengeResolution_throws_for_an_empty_git_workspace_id()
    {
        Assert.Throws<ArgumentException>(() => Attempt.ClaimAgentChallengeResolution(
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.Empty, Guid.NewGuid(),
            "fingerprint-1", Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, BaseTime));
    }

    [Fact]
    public void ClaimAgentChallengeResolution_throws_for_an_empty_context_manifest_artifact_id()
    {
        Assert.Throws<ArgumentException>(() => Attempt.ClaimAgentChallengeResolution(
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(),
            "fingerprint-1", Guid.Empty, TimeSpan.FromMinutes(10), 262144, 524288, BaseTime));
    }

    [Theory]
    [InlineData(AgentOutcome.Accepted)]
    [InlineData(AgentOutcome.Challenged)]
    public void CompleteAgent_completes_successfully_for_a_critical_review_outcome_matching_its_own_contract(AgentOutcome outcome)
    {
        var attempt = ClaimAgentCriticalReviewAttempt(checkpointFingerprintSha256: "fingerprint-1");
        attempt.MarkAgentDispatched(BaseTime.AddSeconds(1));

        attempt.CompleteAgent(outcome, completionFingerprintSha256: "fingerprint-1", BaseTime.AddSeconds(2), TestProcessEvidence.CleanExit);

        Assert.Equal(AttemptStatus.Completed, attempt.Status);
        Assert.Equal(outcome, attempt.AgentOutcome);
        Assert.Equal(BaseTime.AddSeconds(2), attempt.CompletedAtUtc);
    }

    [Theory]
    [InlineData(AgentOutcome.Accepted)]
    [InlineData(AgentOutcome.Challenged)]
    public void CompleteAgent_throws_when_a_critical_review_outcome_is_reported_for_a_planning_attempt(AgentOutcome outcome)
    {
        var attempt = ClaimAgentAttempt(checkpointFingerprintSha256: "fingerprint-1");
        attempt.MarkAgentDispatched(BaseTime.AddSeconds(1));

        Assert.Throws<InvalidOperationException>(
            () => attempt.CompleteAgent(outcome, completionFingerprintSha256: "fingerprint-1", BaseTime.AddSeconds(2)));
    }

    [Fact]
    public void CompleteAgent_throws_when_a_proposed_outcome_is_reported_for_a_critical_review_attempt()
    {
        var attempt = ClaimAgentCriticalReviewAttempt(checkpointFingerprintSha256: "fingerprint-1");
        attempt.MarkAgentDispatched(BaseTime.AddSeconds(1));

        Assert.Throws<InvalidOperationException>(
            () => attempt.CompleteAgent(AgentOutcome.Proposed, completionFingerprintSha256: "fingerprint-1", BaseTime.AddSeconds(2), processEvidence: TestProcessEvidence.CleanExit));
    }

    [Fact]
    public void CompleteAgent_overrides_to_source_changed_for_a_critical_review_attempt_when_the_completion_fingerprint_no_longer_matches()
    {
        var attempt = ClaimAgentCriticalReviewAttempt(checkpointFingerprintSha256: "fingerprint-1");
        attempt.MarkAgentDispatched(BaseTime.AddSeconds(1));

        attempt.CompleteAgent(AgentOutcome.Accepted, completionFingerprintSha256: "fingerprint-2", BaseTime.AddSeconds(2), processEvidence: TestProcessEvidence.CleanExit);

        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.Equal(AgentOutcome.SourceChanged, attempt.AgentOutcome);
    }

    [Fact]
    public void CompleteAgent_throws_for_an_accepted_outcome_on_a_never_dispatched_critical_review_attempt()
    {
        var attempt = ClaimAgentCriticalReviewAttempt(checkpointFingerprintSha256: "fingerprint-1");

        Assert.Throws<InvalidOperationException>(
            () => attempt.CompleteAgent(AgentOutcome.Accepted, completionFingerprintSha256: "fingerprint-1", BaseTime.AddSeconds(1), processEvidence: TestProcessEvidence.CleanExit));
    }

    [Fact]
    public void CompleteAgent_throws_for_an_accepted_outcome_without_a_completion_fingerprint()
    {
        var attempt = ClaimAgentCriticalReviewAttempt();
        attempt.MarkAgentDispatched(BaseTime.AddSeconds(1));

        Assert.Throws<InvalidOperationException>(
            () => attempt.CompleteAgent(AgentOutcome.Accepted, completionFingerprintSha256: null, BaseTime.AddSeconds(2), processEvidence: TestProcessEvidence.CleanExit));
    }

    /// <summary>
    /// <see cref="AgentOutcome.InputAlreadyReviewed"/> is a terminal failure, exactly like
    /// <see cref="AgentOutcome.WorkspaceNoLongerEligible"/> and <see cref="AgentOutcome.SourceChanged"/>
    /// — detected before the provider was ever invoked, so it never requires dispatch or a
    /// completion fingerprint, and it never counts as a Domain-level success regardless of this
    /// attempt's own response contract.
    /// </summary>
    [Fact]
    public void CompleteAgent_completes_with_input_already_reviewed_for_a_never_dispatched_critical_review_attempt()
    {
        var attempt = ClaimAgentCriticalReviewAttempt();

        attempt.CompleteAgent(AgentOutcome.InputAlreadyReviewed, completionFingerprintSha256: null, BaseTime.AddSeconds(1));

        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.Equal(AgentOutcome.InputAlreadyReviewed, attempt.AgentOutcome);
        Assert.Equal(BaseTime.AddSeconds(1), attempt.CompletedAtUtc);
    }

    [Fact]
    public void CompleteAgent_completes_with_input_already_reviewed_for_a_planning_attempt_too()
    {
        // Domain itself never restricts InputAlreadyReviewed to critical-review attempts — it is
        // a plain terminal failure like SourceChanged/WorkspaceNoLongerEligible, so it carries no
        // contract check of its own. The Application-layer closed caller-selectable policy is
        // what actually keeps this outcome scoped to the one dedicated command that ever records
        // it for a real critical-review attempt; this test only proves the Domain-level fact.
        var attempt = ClaimAgentAttempt();

        attempt.CompleteAgent(AgentOutcome.InputAlreadyReviewed, completionFingerprintSha256: null, BaseTime.AddSeconds(1));

        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.Equal(AgentOutcome.InputAlreadyReviewed, attempt.AgentOutcome);
    }

    [Fact]
    public void CompleteAgent_completes_successfully_for_a_resolved_outcome_matching_its_own_contract()
    {
        var attempt = ClaimAgentChallengeResolutionAttempt(checkpointFingerprintSha256: "fingerprint-1");
        attempt.MarkAgentDispatched(BaseTime.AddSeconds(1));

        attempt.CompleteAgent(AgentOutcome.Resolved, completionFingerprintSha256: "fingerprint-1", BaseTime.AddSeconds(2), processEvidence: TestProcessEvidence.CleanExit);

        Assert.Equal(AttemptStatus.Completed, attempt.Status);
        Assert.Equal(AgentOutcome.Resolved, attempt.AgentOutcome);
        Assert.Equal(BaseTime.AddSeconds(2), attempt.CompletedAtUtc);
    }

    [Fact]
    public void CompleteAgent_throws_when_a_resolved_outcome_is_reported_for_a_planning_attempt()
    {
        var attempt = ClaimAgentAttempt(checkpointFingerprintSha256: "fingerprint-1");
        attempt.MarkAgentDispatched(BaseTime.AddSeconds(1));

        Assert.Throws<InvalidOperationException>(
            () => attempt.CompleteAgent(AgentOutcome.Resolved, completionFingerprintSha256: "fingerprint-1", BaseTime.AddSeconds(2), processEvidence: TestProcessEvidence.CleanExit));
    }

    [Fact]
    public void CompleteAgent_throws_when_a_resolved_outcome_is_reported_for_a_critical_review_attempt()
    {
        var attempt = ClaimAgentCriticalReviewAttempt(checkpointFingerprintSha256: "fingerprint-1");
        attempt.MarkAgentDispatched(BaseTime.AddSeconds(1));

        Assert.Throws<InvalidOperationException>(
            () => attempt.CompleteAgent(AgentOutcome.Resolved, completionFingerprintSha256: "fingerprint-1", BaseTime.AddSeconds(2), processEvidence: TestProcessEvidence.CleanExit));
    }

    [Fact]
    public void CompleteAgent_throws_when_a_proposed_outcome_is_reported_for_a_challenge_resolution_attempt()
    {
        var attempt = ClaimAgentChallengeResolutionAttempt(checkpointFingerprintSha256: "fingerprint-1");
        attempt.MarkAgentDispatched(BaseTime.AddSeconds(1));

        Assert.Throws<InvalidOperationException>(
            () => attempt.CompleteAgent(AgentOutcome.Proposed, completionFingerprintSha256: "fingerprint-1", BaseTime.AddSeconds(2), processEvidence: TestProcessEvidence.CleanExit));
    }

    [Theory]
    [InlineData(AgentOutcome.Accepted)]
    [InlineData(AgentOutcome.Challenged)]
    public void CompleteAgent_throws_when_a_critical_review_outcome_is_reported_for_a_challenge_resolution_attempt(AgentOutcome outcome)
    {
        var attempt = ClaimAgentChallengeResolutionAttempt(checkpointFingerprintSha256: "fingerprint-1");
        attempt.MarkAgentDispatched(BaseTime.AddSeconds(1));

        Assert.Throws<InvalidOperationException>(
            () => attempt.CompleteAgent(outcome, completionFingerprintSha256: "fingerprint-1", BaseTime.AddSeconds(2)));
    }

    [Fact]
    public void CompleteAgent_overrides_to_source_changed_for_a_challenge_resolution_attempt_when_the_completion_fingerprint_no_longer_matches()
    {
        var attempt = ClaimAgentChallengeResolutionAttempt(checkpointFingerprintSha256: "fingerprint-1");
        attempt.MarkAgentDispatched(BaseTime.AddSeconds(1));

        attempt.CompleteAgent(AgentOutcome.Resolved, completionFingerprintSha256: "fingerprint-2", BaseTime.AddSeconds(2), processEvidence: TestProcessEvidence.CleanExit);

        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.Equal(AgentOutcome.SourceChanged, attempt.AgentOutcome);
    }

    [Fact]
    public void CompleteAgent_throws_for_a_resolved_outcome_on_a_never_dispatched_challenge_resolution_attempt()
    {
        var attempt = ClaimAgentChallengeResolutionAttempt(checkpointFingerprintSha256: "fingerprint-1");

        Assert.Throws<InvalidOperationException>(
            () => attempt.CompleteAgent(AgentOutcome.Resolved, completionFingerprintSha256: "fingerprint-1", BaseTime.AddSeconds(1), processEvidence: TestProcessEvidence.CleanExit));
    }

    [Fact]
    public void CompleteAgent_throws_for_a_resolved_outcome_without_a_completion_fingerprint()
    {
        var attempt = ClaimAgentChallengeResolutionAttempt();
        attempt.MarkAgentDispatched(BaseTime.AddSeconds(1));

        Assert.Throws<InvalidOperationException>(
            () => attempt.CompleteAgent(AgentOutcome.Resolved, completionFingerprintSha256: null, BaseTime.AddSeconds(2), processEvidence: TestProcessEvidence.CleanExit));
    }

    private static Attempt ClaimAgentImplementationAttempt(
        string checkpointFingerprintSha256 = "fingerprint-1",
        TimeSpan? timeout = null,
        int maxBytesPerStream = 262144,
        int maxTotalCapturedBytes = 524288) => Attempt.ClaimAgentImplementation(
        Guid.NewGuid(),
        Guid.NewGuid(),
        attemptNumber: 1,
        gitWorkspaceId: Guid.NewGuid(),
        gitCheckpointId: Guid.NewGuid(),
        checkpointFingerprintSha256: checkpointFingerprintSha256,
        contextManifestArtifactId: Guid.NewGuid(),
        timeout: timeout ?? TimeSpan.FromMinutes(10),
        maxBytesPerStream: maxBytesPerStream,
        maxTotalCapturedBytes: maxTotalCapturedBytes,
        claimedAtUtc: BaseTime);

    [Fact]
    public void ClaimAgentImplementation_creates_an_implementation_attempt_with_its_durable_intent_persisted()
    {
        var workspaceId = Guid.NewGuid();
        var checkpointId = Guid.NewGuid();
        var manifestArtifactId = Guid.NewGuid();
        var timeout = TimeSpan.FromMinutes(20);

        var attempt = Attempt.ClaimAgentImplementation(
            Guid.NewGuid(),
            Guid.NewGuid(),
            attemptNumber: 1,
            gitWorkspaceId: workspaceId,
            gitCheckpointId: checkpointId,
            checkpointFingerprintSha256: "fingerprint-1",
            contextManifestArtifactId: manifestArtifactId,
            timeout: timeout,
            maxBytesPerStream: 262144,
            maxTotalCapturedBytes: 524288,
            claimedAtUtc: BaseTime);

        Assert.Equal(AttemptKind.Agent, attempt.Kind);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Equal(AgentProvider.ClaudeCode, attempt.AgentProvider);
        Assert.Equal(AgentRole.Implementer, attempt.AgentRole);
        Assert.Equal(AgentResponseContract.ImplementationReport, attempt.AgentResponseContract);
        Assert.Equal(workspaceId, attempt.AgentGitWorkspaceId);
        Assert.Equal(checkpointId, attempt.AgentGitCheckpointId);
        Assert.Equal("fingerprint-1", attempt.AgentCheckpointFingerprintSha256);
        Assert.Equal(manifestArtifactId, attempt.AgentContextManifestArtifactId);
        Assert.Equal(timeout, attempt.AgentTimeout);
        Assert.Null(attempt.AgentResultGitCheckpointId);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void ClaimAgentImplementation_throws_for_a_non_positive_attempt_number(int attemptNumber)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Attempt.ClaimAgentImplementation(
            Guid.NewGuid(), Guid.NewGuid(), attemptNumber, Guid.NewGuid(), Guid.NewGuid(), "fingerprint-1",
            Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, BaseTime));
    }

    [Fact]
    public void CompleteImplementation_completes_successfully_and_records_the_result_checkpoint()
    {
        var attempt = ClaimAgentImplementationAttempt(checkpointFingerprintSha256: "fingerprint-1");
        attempt.MarkAgentDispatched(BaseTime.AddSeconds(1));
        var resultCheckpointId = Guid.NewGuid();

        attempt.CompleteImplementation(AgentOutcome.Implemented, resultCheckpointId, BaseTime.AddSeconds(2), processEvidence: TestProcessEvidence.CleanExit);

        Assert.Equal(AttemptStatus.Completed, attempt.Status);
        Assert.Equal(AgentOutcome.Implemented, attempt.AgentOutcome);
        Assert.Equal(resultCheckpointId, attempt.AgentResultGitCheckpointId);
        Assert.Equal(BaseTime.AddSeconds(2), attempt.CompletedAtUtc);
        // The starting checkpoint is never overwritten by the result.
        Assert.NotEqual(resultCheckpointId, attempt.AgentGitCheckpointId);
    }

    [Fact]
    public void CompleteImplementation_never_overrides_to_source_changed_despite_a_changed_fingerprint()
    {
        // The whole point of this attempt shape: CompleteAgent's fingerprint-mismatch override
        // must never apply here, since a real implementation is expected to change the
        // fingerprint. CompleteImplementation takes no fingerprint parameter at all — the caller
        // has already independently decided the outcome from fresh Git evidence.
        var attempt = ClaimAgentImplementationAttempt(checkpointFingerprintSha256: "fingerprint-1");
        attempt.MarkAgentDispatched(BaseTime.AddSeconds(1));

        attempt.CompleteImplementation(AgentOutcome.Implemented, Guid.NewGuid(), BaseTime.AddSeconds(2), processEvidence: TestProcessEvidence.CleanExit);

        Assert.Equal(AgentOutcome.Implemented, attempt.AgentOutcome);
        Assert.NotEqual(AgentOutcome.SourceChanged, attempt.AgentOutcome);
    }

    [Fact]
    public void CompleteImplementation_completes_as_failed_for_no_changes_produced_with_no_result_checkpoint()
    {
        var attempt = ClaimAgentImplementationAttempt();
        attempt.MarkAgentDispatched(BaseTime.AddSeconds(1));

        attempt.CompleteImplementation(
            AgentOutcome.NoChangesProduced, resultGitCheckpointId: null, BaseTime.AddSeconds(2), processEvidence: TestProcessEvidence.CleanExit);

        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.Equal(AgentOutcome.NoChangesProduced, attempt.AgentOutcome);
        Assert.Null(attempt.AgentResultGitCheckpointId);
    }

    [Theory]
    [InlineData(AgentOutcome.InvalidStructuredOutput)]
    [InlineData(AgentOutcome.ProviderInvocationFailed)]
    [InlineData(AgentOutcome.CheckpointEvidenceUnavailable)]
    public void CompleteImplementation_completes_as_failed_for_every_closed_failure_outcome(AgentOutcome outcome)
    {
        var attempt = ClaimAgentImplementationAttempt();
        attempt.MarkAgentDispatched(BaseTime.AddSeconds(1));

        attempt.CompleteImplementation(
            outcome,
            resultGitCheckpointId: null,
            BaseTime.AddSeconds(2),
            AgentProcessEvidencePolicy.RequiresCleanExit(outcome) ? TestProcessEvidence.CleanExit : null);

        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.Equal(outcome, attempt.AgentOutcome);
    }

    [Fact]
    public void CompleteImplementation_throws_when_implemented_is_reported_for_a_never_dispatched_attempt()
    {
        var attempt = ClaimAgentImplementationAttempt();

        Assert.Throws<InvalidOperationException>(
            () => attempt.CompleteImplementation(AgentOutcome.Implemented, Guid.NewGuid(), BaseTime.AddSeconds(1), processEvidence: TestProcessEvidence.CleanExit));
    }

    [Fact]
    public void CompleteImplementation_throws_when_implemented_is_reported_without_a_result_checkpoint_identity()
    {
        var attempt = ClaimAgentImplementationAttempt();
        attempt.MarkAgentDispatched(BaseTime.AddSeconds(1));

        Assert.Throws<ArgumentException>(
            () => attempt.CompleteImplementation(AgentOutcome.Implemented, resultGitCheckpointId: null, BaseTime.AddSeconds(2), processEvidence: TestProcessEvidence.CleanExit));
    }

    [Fact]
    public void CompleteImplementation_throws_when_a_non_implemented_outcome_carries_a_result_checkpoint_identity()
    {
        var attempt = ClaimAgentImplementationAttempt();
        attempt.MarkAgentDispatched(BaseTime.AddSeconds(1));

        Assert.Throws<ArgumentException>(
            () => attempt.CompleteImplementation(AgentOutcome.NoChangesProduced, Guid.NewGuid(), BaseTime.AddSeconds(2)));
    }

    [Fact]
    public void CompleteImplementation_throws_when_called_for_a_non_implementer_attempt()
    {
        var attempt = ClaimAgentChallengeResolutionAttempt();
        attempt.MarkAgentDispatched(BaseTime.AddSeconds(1));

        Assert.Throws<InvalidOperationException>(
            () => attempt.CompleteImplementation(AgentOutcome.Implemented, Guid.NewGuid(), BaseTime.AddSeconds(2), processEvidence: TestProcessEvidence.CleanExit));
    }

    [Fact]
    public void AgentEffect_is_null_for_a_non_agent_attempt()
    {
        var processAttempt = Attempt.ClaimProcess(Guid.NewGuid(), Guid.NewGuid(), 1, CreateIntent(), BaseTime);
        var simulatedAttempt = Attempt.Claim(Guid.NewGuid(), Guid.NewGuid(), 1, BaseTime);

        Assert.Null(processAttempt.AgentEffect);
        Assert.Null(simulatedAttempt.AgentEffect);
    }

    [Fact]
    public void AgentEffect_is_read_only_for_every_non_implementer_claim_factory()
    {
        Assert.Equal(AgentEffectKind.ReadOnly, ClaimAgentAttempt().AgentEffect);
        Assert.Equal(AgentEffectKind.ReadOnly, ClaimAgentCriticalReviewAttempt().AgentEffect);
        Assert.Equal(AgentEffectKind.ReadOnly, ClaimAgentChallengeResolutionAttempt().AgentEffect);
        Assert.Equal(
            AgentEffectKind.ReadOnly,
            Attempt.ClaimAgentCodeReview(
                Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), "fingerprint-1",
                Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, BaseTime).AgentEffect);
    }

    [Fact]
    public void AgentEffect_is_workspace_mutating_for_the_implementer_claim_factory()
    {
        Assert.Equal(AgentEffectKind.WorkspaceMutating, ClaimAgentImplementationAttempt().AgentEffect);
    }

    // Every ClaimAgent* factory must set Role and ResponseContract from the same
    // AgentAttemptContract its AgentEffect is computed from — never a value that could drift apart
    // from the contract table.
    [Fact]
    public void Every_claim_agent_factory_produces_role_response_contract_and_effect_matching_its_own_contract()
    {
        AssertMatchesItsOwnContract(ClaimAgentAttempt());
        AssertMatchesItsOwnContract(ClaimAgentCriticalReviewAttempt());
        AssertMatchesItsOwnContract(ClaimAgentChallengeResolutionAttempt());
        AssertMatchesItsOwnContract(ClaimAgentImplementationAttempt());
        AssertMatchesItsOwnContract(
            Attempt.ClaimAgentCodeReview(
                Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), "fingerprint-1",
                Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, BaseTime));
        AssertMatchesItsOwnContract(
            Attempt.ClaimAgentReviewCorrection(
                Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), "fingerprint-1",
                Guid.NewGuid(), TimeSpan.FromMinutes(20), 262144, 524288, BaseTime));

        static void AssertMatchesItsOwnContract(Attempt attempt)
        {
            var contract = AgentAttemptContract.For(attempt.AgentResponseContract!.Value);
            Assert.Equal(contract.Role, attempt.AgentRole);
            Assert.Equal(contract.ResponseContract, attempt.AgentResponseContract);
            Assert.Equal(contract.Effect, attempt.AgentEffect);
        }
    }

    // This proves the new ReadOnly-effect guard added to CompleteAgent per the role-first
    // stabilization: it is never reachable through any production call path today (only
    // CompleteImplementation ever completes an Implementer attempt), but it is now an explicit,
    // enforced Domain invariant rather than an implicit assumption.
    [Fact]
    public void CompleteAgent_throws_for_a_workspace_mutating_contract()
    {
        var attempt = ClaimAgentImplementationAttempt(checkpointFingerprintSha256: "fingerprint-1");
        attempt.MarkAgentDispatched(BaseTime.AddSeconds(1));

        Assert.Throws<InvalidOperationException>(
            () => attempt.CompleteAgent(AgentOutcome.Implemented, completionFingerprintSha256: "fingerprint-1", BaseTime.AddSeconds(2), processEvidence: TestProcessEvidence.CleanExit));
    }
}
