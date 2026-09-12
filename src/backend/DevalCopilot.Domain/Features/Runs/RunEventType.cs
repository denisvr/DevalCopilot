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
}
