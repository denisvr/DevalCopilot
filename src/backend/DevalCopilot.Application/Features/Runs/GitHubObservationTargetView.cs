namespace DevalCopilot.Application.Features.Runs;

/// <summary>The immutable Human selection (ADR-0040): the one event that recorded it, the fixed host, the selected owner and repository
/// and the UTC recording time. It is local scope selection, never verified remote identity, permission, CI or publication authority.</summary>
public sealed record GitHubObservationTargetView(
    Guid Id, Guid EventId, string Host, string Owner, string Repository, DateTimeOffset RecordedAtUtc);
