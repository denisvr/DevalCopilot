namespace DevalCopilot.Api.Features.Runs.GetGitHubBranchPublicationApproval;

/// <summary>The recorded ADR-0043 binding the approval refers to: its identifier, the one event that recorded it, the opaque node
/// identifier, the names the host observed and the UTC observation and recording times. It is historical identity evidence, never a fresh
/// remote check or permission.</summary>
public sealed record GitHubBranchPublicationApprovalBindingResponse(
    Guid Id,
    Guid EventId,
    string RepositoryNodeId,
    string Owner,
    string Repository,
    DateTimeOffset ObservedAtUtc,
    DateTimeOffset RecordedAtUtc);
