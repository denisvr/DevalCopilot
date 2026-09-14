namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The bounded, deterministic simulated-agent-collaboration event sequence for the
/// walking-skeleton run. Later increments replace these with real protocol messages;
/// the string values are a durable schema, not a UI label.
/// </summary>
public static class RunEventType
{
    public const string RunStarted = "run.started";
    public const string CodexProposal = "codex.proposal";
    public const string ClaudeChallenge = "claude.challenge";
    public const string CodexResolution = "codex.resolution";
    public const string ClaudeExecution = "claude.execution";
    public const string RunCompleted = "run.completed";

    /// <summary>Metadata-only fact: an <see cref="Artifact"/> was durably recorded for an
    /// attempt. The payload carries the artifact id, purpose, byte length, truncation flag, and
    /// capture outcome — never captured content.</summary>
    public const string ProcessOutputCaptured = "process.output_captured";

    /// <summary>Metadata-only fact: a Process attempt reached a terminal state with no artifact
    /// ever recorded — the adapter failed before a child process or output sink existed, so
    /// there was never anything to seal or capture. The payload carries only the attempt's
    /// final status; never exception text, arguments, environment values, or output.</summary>
    public const string ProcessEndedWithoutOutput = "process.ended_without_output";
}
