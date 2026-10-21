using DevalCopilot.Domain.Features.Projects;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Projects;

/// <summary>ADR-0060: the dedicated pre-dispatch status of a local-execution-sequence verification refusal carries no process outcome,
/// exit code or completion fingerprint.</summary>
public sealed class VerificationExecutionLocalExecutionSequenceRefusalTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('b', 64);

    private static VerificationExecution Running()
    {
        var project = Project.Register(Guid.NewGuid(), "Project", @"C:\repos\Project", Now);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, @"C:\workspaces\Project", "devalcopilot/workspace/1/1",
            new string('a', 40), "main", Now);
        workspace.MarkReady();
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), Fingerprint, []);
        var command = VerificationCommand.Configure(
            Guid.NewGuid(), project.Id, 1, "Tests", @"C:\tools\dotnet.exe", [], 120, true, Now);
        return VerificationExecution.Claim(Guid.NewGuid(), project.Id, 1, workspace, checkpoint, command, Now);
    }

    [Fact]
    public void An_undispatched_running_execution_ends_with_the_dedicated_status_and_no_process_evidence()
    {
        var execution = Running();

        execution.MarkLocalExecutionSequenceDispatchRefused(Now.AddMinutes(1));

        Assert.Equal(VerificationExecutionStatus.LocalExecutionSequenceDispatchRefused, execution.Status);
        Assert.Equal(Now.AddMinutes(1), execution.CompletedAtUtc);
        Assert.Null(execution.DispatchedAtUtc);
        Assert.Null(execution.Outcome);
        Assert.Null(execution.ExitCode);
        Assert.Null(execution.CompletionFingerprintSha256);
    }

    [Fact]
    public void A_dispatched_execution_cannot_record_the_refusal()
    {
        var execution = Running();
        execution.MarkDispatched(Now);

        Assert.Throws<InvalidOperationException>(() => execution.MarkLocalExecutionSequenceDispatchRefused(Now));
        Assert.Equal(VerificationExecutionStatus.Running, execution.Status);
    }

    [Fact]
    public void A_terminal_execution_cannot_record_the_refusal_and_the_refusal_is_recorded_once()
    {
        var changed = Running();
        changed.MarkSourceChangedBeforeDispatch(Fingerprint, Now);
        Assert.Throws<InvalidOperationException>(() => changed.MarkLocalExecutionSequenceDispatchRefused(Now));

        var refused = Running();
        refused.MarkLocalExecutionSequenceDispatchRefused(Now);
        Assert.Throws<InvalidOperationException>(() => refused.MarkLocalExecutionSequenceDispatchRefused(Now));
    }

    [Fact]
    public void The_status_value_is_appended_without_renumbering()
    {
        Assert.Equal(6, (int)VerificationExecutionStatus.SourceChanged);
        Assert.Equal(7, (int)VerificationExecutionStatus.LocalExecutionSequenceDispatchRefused);
        Assert.Equal(7, Enum.GetValues<VerificationExecutionStatus>().Max(status => (int)status));
    }

    [Fact]
    public void Review_evidence_accepts_the_status_without_an_outcome_or_exit_code()
    {
        var evidence = CheckpointReviewEvidence.Observe(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, Fingerprint,
            VerificationExecutionStatus.LocalExecutionSequenceDispatchRefused, verificationExecutionOutcome: null,
            verificationExecutionExitCode: null);

        Assert.Equal(VerificationExecutionStatus.LocalExecutionSequenceDispatchRefused, evidence.VerificationExecutionStatus);
        Assert.Null(evidence.VerificationExecutionOutcome);
    }

    [Theory]
    [InlineData(VerificationExecutionOutcome.Exited, 0)]
    [InlineData(VerificationExecutionOutcome.Exited, 1)]
    [InlineData(VerificationExecutionOutcome.TimedOut, null)]
    public void Review_evidence_rejects_a_process_outcome_with_the_status(VerificationExecutionOutcome outcome, int? exitCode)
    {
        Assert.Throws<ArgumentException>(() => CheckpointReviewEvidence.Observe(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, Fingerprint,
            VerificationExecutionStatus.LocalExecutionSequenceDispatchRefused, outcome, exitCode));
    }
}
