using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Api.Features.Runs;

/// <summary>
/// Host-measured execution evidence for one Agent attempt's provider child process, exposed as a
/// sibling of the attempt's semantic <c>outcome</c> and never a replacement for it.
/// <c>Outcome</c> is <c>Exited</c>, <c>TimedOut</c>, or <c>Cancelled</c>, or null when the evidence
/// is absent or unknown; <c>ExitCode</c> is present only for <c>Exited</c>. Never carries a path,
/// argument, environment value, output, manifest, session identifier, or credential.
/// </summary>
public sealed record AgentProcessExecutionResponse(
    string? Outcome,
    int? ExitCode,
    long? DurationMilliseconds,
    long? TimeoutMilliseconds)
{
    /// <summary>An existing attempt always carries its configured timeout; the evidence members
    /// stay null while the evidence is absent or unknown. The durable evidence keeps the host's
    /// exact tick-resolution measurement (see <c>AttemptConfiguration.AgentProcessDuration</c>);
    /// this bounded projection truncates it toward zero to whole milliseconds — sub-millisecond
    /// precision is never meaningful to a human reading this field, but the truncation direction is
    /// documented here so a caller never mistakes it for rounding.</summary>
    public static AgentProcessExecutionResponse FromAttempt(AgentProcessExecutionEvidence? evidence, TimeSpan? timeout) =>
        new(
            evidence?.Outcome.ToString(),
            evidence?.ExitCode,
            evidence is null ? null : (long)evidence.Duration.TotalMilliseconds,
            timeout is null ? null : (long)timeout.Value.TotalMilliseconds);

    /// <summary>Null only when there is no attempt at all.</summary>
    public static AgentProcessExecutionResponse? FromDomain(bool hasAttempt, AgentProcessExecutionEvidence? evidence, TimeSpan? timeout) =>
        hasAttempt ? FromAttempt(evidence, timeout) : null;
}
