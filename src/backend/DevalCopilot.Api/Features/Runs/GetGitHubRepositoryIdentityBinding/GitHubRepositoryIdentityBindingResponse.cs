namespace DevalCopilot.Api.Features.Runs.GetGitHubRepositoryIdentityBinding;

/// <summary>The immutable Human confirmation of a GitHub repository identity (ADR-0043): its identifier, the one event that recorded it, the
/// opaque node identifier, the names the host observed and the UTC observation and recording times. It is historical identity evidence,
/// never a fresh remote check, permission, branch or commit proof, CI approval or publication authority.</summary>
public sealed record GitHubRepositoryIdentityBindingResponse(
    Guid Id,
    Guid EventId,
    string RepositoryNodeId,
    string Owner,
    string Repository,
    DateTimeOffset ObservedAtUtc,
    DateTimeOffset RecordedAtUtc);
