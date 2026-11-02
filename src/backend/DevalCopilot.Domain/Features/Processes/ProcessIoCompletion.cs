namespace DevalCopilot.Domain.Features.Processes;

/// <summary>
/// ADR-0072: the host's I/O completion facts of one execution, separate from its outcome, exit code, physical termination,
/// duration and truncation flags. Only <see cref="IsComplete"/> I/O may support a semantic success, a successful Process
/// completion or a verification pass; an observed exit code zero with incomplete I/O stays recorded as a physical fact and is a
/// semantic failure.
/// </summary>
public sealed record ProcessIoCompletion(
    OutputCaptureCompletion StandardOutput,
    OutputCaptureCompletion StandardError,
    InputDeliveryCompletion StandardInput)
{
    /// <summary>No I/O completion was established, as for an adapter that never observed a stream.</summary>
    public static ProcessIoCompletion Unknown { get; } =
        new(OutputCaptureCompletion.Unknown, OutputCaptureCompletion.Unknown, InputDeliveryCompletion.Unknown);

    /// <summary>Both output streams ended and no standard input was redirected.</summary>
    public static ProcessIoCompletion CompleteWithoutInput { get; } =
        new(OutputCaptureCompletion.Complete, OutputCaptureCompletion.Complete, InputDeliveryCompletion.NotRedirected);

    /// <summary>Both output streams ended and the supplied standard input was delivered and closed.</summary>
    public static ProcessIoCompletion CompleteWithInput { get; } =
        new(OutputCaptureCompletion.Complete, OutputCaptureCompletion.Complete, InputDeliveryCompletion.Complete);

    /// <summary>True only when both output streams are <see cref="OutputCaptureCompletion.Complete"/> and standard input was
    /// either delivered completely or never redirected.</summary>
    public bool IsComplete =>
        StandardOutput == OutputCaptureCompletion.Complete
        && StandardError == OutputCaptureCompletion.Complete
        && StandardInput is InputDeliveryCompletion.Complete or InputDeliveryCompletion.NotRedirected;

    /// <summary>True only for a stream whose writer the host proved closed (Complete, DeadlineExceeded or Faulted), so that its
    /// partial output file may be sealed; an <see cref="OutputCaptureCompletion.Unknown"/> or absent fact never is.</summary>
    public static bool ProvesWriterClosed(OutputCaptureCompletion? completion) =>
        completion is OutputCaptureCompletion.Complete or OutputCaptureCompletion.DeadlineExceeded or OutputCaptureCompletion.Faulted;

    /// <summary>True when any stream's bounded completion window expired.</summary>
    public bool DeadlineExceeded =>
        StandardOutput == OutputCaptureCompletion.DeadlineExceeded
        || StandardError == OutputCaptureCompletion.DeadlineExceeded
        || StandardInput == InputDeliveryCompletion.DeadlineExceeded;
}
