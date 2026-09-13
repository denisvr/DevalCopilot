namespace DevalCopilot.Application.Features.Runs.Queries.GetEligibleProcessAttempts;

/// <summary>
/// Enough of a claimed Process attempt's persisted, non-secret intent for the hosted
/// supervisor to reconstruct a <c>ProcessExecutionRequest</c> — with no environment
/// variables, since this slice never persists them.
/// </summary>
public sealed record EligibleProcessAttempt(
    Guid AttemptId,
    Guid RunId,
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    string ApprovedRoot,
    TimeSpan Timeout,
    int MaxBytesPerStream,
    int MaxTotalCapturedBytes);
