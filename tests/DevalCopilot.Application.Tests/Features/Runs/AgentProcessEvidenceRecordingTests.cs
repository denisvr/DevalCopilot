using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed class AgentProcessEvidenceRecordingTests
{
    [Fact]
    public void FromProcessExecutionResult_preserves_outcome_exit_code_and_host_measured_duration_only()
    {
        var result = new ProcessExecutionResult
        {
            Outcome = ProcessExecutionOutcome.Exited,
            ExitCode = 3,
            StandardOutput = "raw provider output that must never cross into evidence",
            StandardOutputTruncated = true,
            StandardError = "raw error",
            StandardErrorTruncated = false,
            Duration = TimeSpan.FromMilliseconds(4321),
        };

        var evidence = AgentProcessEvidence.FromProcessExecutionResult(result);

        Assert.Equal(new AgentProcessEvidence(ProcessExecutionOutcome.Exited, 3, TimeSpan.FromMilliseconds(4321)), evidence);
        Assert.False(evidence.IsCleanExit);
    }

    [Theory]
    [InlineData(ProcessExecutionOutcome.Exited, null, 10)]
    [InlineData(ProcessExecutionOutcome.TimedOut, 0, 10)]
    [InlineData(ProcessExecutionOutcome.Cancelled, 1, 10)]
    [InlineData(ProcessExecutionOutcome.Exited, 0, -5)]
    [InlineData((ProcessExecutionOutcome)42, null, 10)]
    public void Validate_rejects_every_invalid_evidence_shape(ProcessExecutionOutcome outcome, int? exitCode, long durationMilliseconds)
    {
        var error = AgentProcessEvidenceRecording.Validate(
            new AgentProcessEvidence(outcome, exitCode, TimeSpan.FromMilliseconds(durationMilliseconds)),
            AgentOutcome.ProviderInvocationFailed,
            dispatched: true,
            out var domainEvidence);

        Assert.Equal(AgentProcessEvidenceRecording.InvalidEvidenceCode, error?.Code);
        Assert.Null(domainEvidence);
    }

    [Theory]
    [InlineData(AgentOutcome.Proposed)]
    [InlineData(AgentOutcome.Accepted)]
    [InlineData(AgentOutcome.Resolved)]
    [InlineData(AgentOutcome.Implemented)]
    [InlineData(AgentOutcome.ReviewApproved)]
    [InlineData(AgentOutcome.CorrectionApplied)]
    [InlineData(AgentOutcome.InvalidStructuredOutput)]
    public void Validate_requires_clean_exit_evidence_for_success_and_invalid_output(AgentOutcome outcome)
    {
        var missing = AgentProcessEvidenceRecording.Validate(null, outcome, dispatched: true, out _);
        var nonZero = AgentProcessEvidenceRecording.Validate(
            new AgentProcessEvidence(ProcessExecutionOutcome.Exited, 1, TimeSpan.Zero), outcome, dispatched: true, out _);
        var timedOut = AgentProcessEvidenceRecording.Validate(
            new AgentProcessEvidence(ProcessExecutionOutcome.TimedOut, null, TimeSpan.Zero), outcome, dispatched: true, out _);
        var clean = AgentProcessEvidenceRecording.Validate(TestProcessEvidence.ReportedCleanExit, outcome, dispatched: true, out var evidence);

        Assert.Equal(AgentProcessEvidenceRecording.CleanExitRequiredCode, missing?.Code);
        Assert.Equal(AgentProcessEvidenceRecording.CleanExitRequiredCode, nonZero?.Code);
        Assert.Equal(AgentProcessEvidenceRecording.CleanExitRequiredCode, timedOut?.Code);
        Assert.Null(clean);
        Assert.Equal(TestProcessEvidence.CleanExit, evidence);
    }

    [Fact]
    public void Validate_rejects_evidence_for_an_undispatched_attempt()
    {
        var error = AgentProcessEvidenceRecording.Validate(
            TestProcessEvidence.ReportedCleanExit, AgentOutcome.ProviderInvocationFailed, dispatched: false, out _);

        Assert.Equal(AgentProcessEvidenceRecording.EvidenceWithoutDispatchCode, error?.Code);
    }

    // Defect-2 regression: a pre-invocation outcome is rejected by its own dedicated code, never
    // the generic "not dispatched" code, and regardless of the attempt's dispatch state.
    [Theory]
    [InlineData(AgentOutcome.WorkspaceNoLongerEligible)]
    [InlineData(AgentOutcome.InputAlreadyReviewed)]
    [InlineData(AgentOutcome.InputAlreadyResolved)]
    [InlineData(AgentOutcome.InputAlreadyImplemented)]
    [InlineData(AgentOutcome.InputAlreadyCodeReviewed)]
    [InlineData(AgentOutcome.InputAlreadyCorrected)]
    public void Validate_rejects_evidence_for_every_pre_invocation_outcome_regardless_of_dispatch_state(AgentOutcome outcome)
    {
        // domainEvidence is only meaningful to a caller when the returned Error is null — every
        // real call site returns the failure immediately without reading it — so only the error
        // codes are asserted for the two rejected combinations below.
        var dispatchedError = AgentProcessEvidenceRecording.Validate(
            TestProcessEvidence.ReportedCleanExit, outcome, dispatched: true, out _);
        var undispatchedError = AgentProcessEvidenceRecording.Validate(
            TestProcessEvidence.ReportedCleanExit, outcome, dispatched: false, out _);

        Assert.Equal(AgentProcessEvidenceRecording.PreInvocationOutcomeCannotCarryEvidenceCode, dispatchedError?.Code);
        Assert.Equal(AgentProcessEvidenceRecording.PreInvocationOutcomeCannotCarryEvidenceCode, undispatchedError?.Code);

        var withoutEvidenceError = AgentProcessEvidenceRecording.Validate(null, outcome, dispatched: false, out var noEvidence);
        Assert.Null(withoutEvidenceError);
        Assert.Null(noEvidence);
    }

    // Defect-1 regression at the Application boundary: these four classifications are only ever
    // reached once the process is already known to have exited cleanly, so they must be rejected
    // exactly like a real success outcome when clean-exit evidence is missing or contradicted.
    [Theory]
    [InlineData(AgentOutcome.NoChangesProduced)]
    [InlineData(AgentOutcome.ImplementationHeadChanged)]
    [InlineData(AgentOutcome.CorrectionNoChangesProduced)]
    [InlineData(AgentOutcome.CorrectionHeadChanged)]
    public void Validate_requires_clean_exit_evidence_for_every_process_succeeded_failure_classification(AgentOutcome outcome)
    {
        var missing = AgentProcessEvidenceRecording.Validate(null, outcome, dispatched: true, out _);
        var nonZero = AgentProcessEvidenceRecording.Validate(
            new AgentProcessEvidence(ProcessExecutionOutcome.Exited, 1, TimeSpan.Zero), outcome, dispatched: true, out _);
        var clean = AgentProcessEvidenceRecording.Validate(TestProcessEvidence.ReportedCleanExit, outcome, dispatched: true, out var evidence);

        Assert.Equal(AgentProcessEvidenceRecording.CleanExitRequiredCode, missing?.Code);
        Assert.Equal(AgentProcessEvidenceRecording.CleanExitRequiredCode, nonZero?.Code);
        Assert.Null(clean);
        Assert.Equal(TestProcessEvidence.CleanExit, evidence);
    }

    [Fact]
    public void Validate_maps_each_port_outcome_to_the_matching_domain_outcome()
    {
        foreach (var (portOutcome, domainOutcome, exitCode) in new[]
                 {
                     (ProcessExecutionOutcome.Exited, ProcessOutcome.Exited, (int?)9),
                     (ProcessExecutionOutcome.TimedOut, ProcessOutcome.TimedOut, null),
                     (ProcessExecutionOutcome.Cancelled, ProcessOutcome.Cancelled, null),
                 })
        {
            var error = AgentProcessEvidenceRecording.Validate(
                new AgentProcessEvidence(portOutcome, exitCode, TimeSpan.FromSeconds(2)),
                AgentOutcome.ProviderInvocationFailed,
                dispatched: true,
                out var evidence);

            Assert.Null(error);
            Assert.Equal(AgentProcessExecutionEvidence.Create(domainOutcome, exitCode, TimeSpan.FromSeconds(2)), evidence);
        }
    }

    [Fact]
    public void Validate_accepts_absent_evidence_for_a_failure_outcome()
    {
        var error = AgentProcessEvidenceRecording.Validate(null, AgentOutcome.ProviderInvocationFailed, dispatched: true, out var evidence);

        Assert.Null(error);
        Assert.Null(evidence);
    }
}
