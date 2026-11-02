namespace DevalCopilot.Domain.Features.Processes;

/// <summary>
/// ADR-0072: how the host's delivery of standard input to a child process ended. <see cref="NotRedirected"/> means the request
/// supplied no input and standard input was never redirected; every other value describes an actual delivery. Unknown is the
/// default, so a value that was never established is never read as delivered.
/// </summary>
public enum InputDeliveryCompletion
{
    /// <summary>The host could not establish how the delivery ended.</summary>
    Unknown = 0,

    /// <summary>The request supplied no standard input, so none was redirected or delivered.</summary>
    NotRedirected = 1,

    /// <summary>Every supplied byte was written and the input stream was closed, signalling end-of-file to the child.</summary>
    Complete = 2,

    /// <summary>The write had not finished when its bound (the execution deadline or the I/O completion window) expired.</summary>
    DeadlineExceeded = 3,

    /// <summary>The write failed, for example because the child closed its end before reading everything.</summary>
    Faulted = 4,
}
