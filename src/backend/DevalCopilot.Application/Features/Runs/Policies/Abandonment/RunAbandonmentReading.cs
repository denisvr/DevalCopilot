namespace DevalCopilot.Application.Features.Runs.Policies.Abandonment;

/// <summary>One Abandoned run as <see cref="RunAbandonmentReader"/> read it, with the coherence verdict of
/// <c>RunAbandonmentPolicy.IsCoherent</c>. <paramref name="AbandonedAtUtc"/> is <see langword="null"/> when the stored time is absent
/// or could not be read as a time, which is never coherent.</summary>
internal sealed record RunAbandonmentReading(
    Guid RunId,
    Guid ProjectId,
    int ExecutionNumber,
    string? Reason,
    DateTimeOffset? AbandonedAtUtc,
    bool Coherent);
