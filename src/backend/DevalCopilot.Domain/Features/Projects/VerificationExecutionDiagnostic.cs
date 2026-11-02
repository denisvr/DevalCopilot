namespace DevalCopilot.Domain.Features.Projects;

/// <summary>
/// ADR-0072: the closed reason a dispatched verification execution failed without a meaningful exit, or failed despite one.
/// It never carries a path, environment value, output or exception, and never grants a retry. <see langword="null"/> on an
/// execution means not recorded (every execution before this existed, and every failure explained by its own exit code).
/// </summary>
public enum VerificationExecutionDiagnostic
{
    /// <summary>A required prerequisite of the fixed verification environment profile could not be derived or validated, so no
    /// process was started.</summary>
    EnvironmentUnavailable = 0,

    /// <summary>The executable could not be started.</summary>
    InvocationUnavailable = 1,

    /// <summary>A stream had not completed when the bounded I/O completion window expired.</summary>
    CaptureDeadlineExceeded = 2,

    /// <summary>Reading a stream or writing its accepted bytes failed, or completion could not be established.</summary>
    CaptureFault = 3,
}
