namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// What an <see cref="Artifact"/> durably captured. Closed and process-output-only for this
/// slice; future producers (Git diffs, screenshots, CI logs) extend this enum rather than
/// introducing a parallel record type.
/// </summary>
public enum ArtifactPurpose
{
    ProcessStandardOutput = 0,
    ProcessStandardError = 1,

    /// <summary>The bounded, versioned input given to an Agent attempt's provider — never the
    /// repository, a raw transcript, or credentials. See <c>docs/architecture/agent-collaboration-protocol.md</c>.</summary>
    AgentContextManifest = 2,

    /// <summary>An Agent attempt's raw captured stdout (JSONL for Codex's <c>--json</c> mode),
    /// redacted the same way as <see cref="ProcessStandardOutput"/> before a byte is written.</summary>
    AgentStandardOutput = 3,

    /// <summary>An Agent attempt's raw captured stderr, redacted the same way as
    /// <see cref="ProcessStandardError"/>.</summary>
    AgentStandardError = 4,

    /// <summary>The provider's final structured response file (Codex's <c>--output-last-message</c>
    /// target) — raw provider content, redacted the same way as the other Agent artifacts before
    /// being sealed, independent of whether it later passes protocol/schema validation.</summary>
    AgentFinalResponse = 5,
}
