namespace DevalCopilot.Application.Features.Runs;

/// <summary>Attempt facts that live in non-enum columns and can therefore always be read, even when
/// another column of the same row holds a string EF cannot convert back to its enum.</summary>
public sealed record AgentAttemptScalars(
    Guid Id,
    int AttemptNumber,
    DateTimeOffset ClaimedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset? DispatchedAtUtc);
