using DevalCopilot.Application.Features.Processes.Ports;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionSequences;

/// <summary>What a verification recipe invocation does: it exits with a code and output, ends without an exit proof, throws, or holds
/// until the test releases it.</summary>
internal sealed record LocalExecutionSequenceVerificationBehavior(
    ProcessExecutionOutcome Outcome,
    int? ExitCode,
    string Stdout,
    string Stderr,
    bool Throws = false,
    TaskCompletionSource? Hold = null)
{
    public static LocalExecutionSequenceVerificationBehavior Passes { get; } = new(ProcessExecutionOutcome.Exited, 0, "all tests passed", string.Empty);

    public static LocalExecutionSequenceVerificationBehavior Fails { get; } =
        new(ProcessExecutionOutcome.Exited, 1, LocalExecutionSequenceResponses.FailureLine, "SENTINEL-STDERR build failed");

    public static LocalExecutionSequenceVerificationBehavior TimesOut { get; } = new(ProcessExecutionOutcome.TimedOut, null, string.Empty, string.Empty);

    public static LocalExecutionSequenceVerificationBehavior IsCancelled { get; } = new(ProcessExecutionOutcome.Cancelled, null, string.Empty, string.Empty);

    public static LocalExecutionSequenceVerificationBehavior AdapterThrows { get; } = new(ProcessExecutionOutcome.Exited, null, string.Empty, string.Empty, true);
}
