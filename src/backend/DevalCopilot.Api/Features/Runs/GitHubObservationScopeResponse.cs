namespace DevalCopilot.Api.Features.Runs;

/// <summary>The immutable Human selection of a GitHub repository scope (ADR-0040): its identifier, the one event that recorded it, the
/// fixed host, the selected owner and repository and the UTC recording time. It is local scope selection, never verified remote
/// identity, permission, CI success or publication authority.</summary>
public sealed record GitHubObservationScopeResponse(
    Guid Id, Guid EventId, string Host, string Owner, string Repository, DateTimeOffset RecordedAtUtc);
