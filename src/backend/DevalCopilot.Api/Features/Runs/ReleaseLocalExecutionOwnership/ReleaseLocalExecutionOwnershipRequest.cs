namespace DevalCopilot.Api.Features.Runs.ReleaseLocalExecutionOwnership;

/// <summary>The only caller-supplied facts of an explicit ownership release (ADR-0065): exactly four required canonical strings, the
/// observation and the versioned confirmation with the UTC expiry the Human saw. There is no free text, rationale, command, provider
/// choice, process identifier or path. Unknown, duplicate, differently cased, escaped and mistyped members and an oversized body are
/// refused before anything is dispatched.</summary>
public sealed record ReleaseLocalExecutionOwnershipRequest
{
    public required string ObservationId { get; init; }

    public required string ConfirmationVersion { get; init; }

    public required string ConfirmationSha256 { get; init; }

    public required string ConfirmationExpiresAtUtc { get; init; }
}
