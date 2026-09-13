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
}
