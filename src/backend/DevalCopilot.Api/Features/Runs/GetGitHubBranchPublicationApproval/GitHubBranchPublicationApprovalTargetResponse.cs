namespace DevalCopilot.Api.Features.Runs.GetGitHubBranchPublicationApproval;

/// <summary>The recorded ADR-0040 target the approval refers to: its identifier, the one event that recorded it, the fixed host, the
/// selected owner and repository and the UTC recording time. It is local scope selection, never verified remote identity or permission.</summary>
public sealed record GitHubBranchPublicationApprovalTargetResponse(
    Guid Id, Guid EventId, string Host, string Owner, string Repository, DateTimeOffset RecordedAtUtc);
