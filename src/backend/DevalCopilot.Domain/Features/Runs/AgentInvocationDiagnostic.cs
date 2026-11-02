namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// ADR-0072: the closed, versioned category of why a Claude invocation did not produce semantic success, recorded atomically
/// with its failed result. It describes what the host observed or what the provider reported through a documented field; it is
/// never a verified root cause, never carries provider text, payload, exception, environment value, credential, account
/// identifier or path, and never grants a retry, an authentication action or any other authority. An attempt recorded before
/// diagnostics existed has none (not recorded), which is never read as any category.
/// </summary>
public enum AgentInvocationDiagnostic
{
    /// <summary>The launch component could not be revalidated, its input could not be prepared, or the process could not be
    /// started.</summary>
    InvocationUnavailable = 0,

    /// <summary>The required process environment could not be derived from the system boundary.</summary>
    EnvironmentUnavailable = 1,

    /// <summary>The invocation exceeded its execution deadline and its owned tree was asked to terminate.</summary>
    TimedOut = 2,

    /// <summary>The invocation was cancelled and its owned tree was asked to terminate.</summary>
    Cancelled = 3,

    /// <summary>A redirected stream or the input delivery had not completed when the bounded I/O window expired.</summary>
    CaptureDeadlineExceeded = 4,

    /// <summary>Reading a stream, writing its accepted bytes or delivering input failed, or completion could not be
    /// established.</summary>
    CaptureFault = 5,

    /// <summary>The documented result envelope reported an error with the documented HTTP status 401.</summary>
    AuthenticationRequired = 6,

    /// <summary>The documented result envelope reported an error with the documented HTTP status 429.</summary>
    ProviderLimitReached = 7,

    /// <summary>The documented result envelope reported an error without a more specific documented signal.</summary>
    ProviderReportedError = 8,

    /// <summary>The process exited with a non-zero code and no interpretable error envelope.</summary>
    NonZeroExit = 9,

    /// <summary>The output was not a single well-formed, coherent result envelope (including truncated, duplicated, malformed
    /// or contradictory facts).</summary>
    InvalidEnvelope = 10,

    /// <summary>A successful envelope carried no structured output.</summary>
    StructuredOutputMissing = 11,

    /// <summary>A successful envelope carried structured output that was not a JSON object.</summary>
    InvalidStructuredOutput = 12,
}
