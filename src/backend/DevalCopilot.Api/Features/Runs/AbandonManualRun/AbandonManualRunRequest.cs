namespace DevalCopilot.Api.Features.Runs.AbandonManualRun;

/// <summary>The only caller-supplied fact of an explicit abandonment: the human reason, trimmed with CRLF normalized to LF, nonblank,
/// free of control characters other than LF and at most 2 KiB of UTF-8. The host decides the mode, lifecycle, project, times and
/// participants.</summary>
public sealed record AbandonManualRunRequest
{
    public required string Reason { get; init; }
}
