namespace DevalCopilot.Application.Features.Runs.Policies.GitHubObservation;

/// <summary>The immutable Human confirmation (ADR-0043): the one event that recorded it, the opaque node identifier, the names the host
/// observed and the UTC observation and recording times. It is historical identity evidence, never a fresh remote check, permission, branch
/// or commit proof, CI approval or publication authority.</summary>
public sealed record GitHubRepositoryIdentityBindingView(
    Guid Id,
    Guid EventId,
    string RepositoryNodeId,
    string Owner,
    string Repository,
    DateTimeOffset ObservedAtUtc,
    DateTimeOffset RecordedAtUtc);
