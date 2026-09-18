namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The bounded deterministic event journal for the walking-skeleton run. Typed protocol
/// envelopes supplement these operational facts; the string values are durable schema, not UI labels.
/// </summary>
public static class RunEventType
{
    public const string RunStarted = "run.started";
    public const string CodexProposal = "codex.proposal";
    public const string ClaudeChallenge = "claude.challenge";
    public const string CodexResolution = "codex.resolution";
    public const string ClaudeExecution = "claude.execution";
    public const string RunCompleted = "run.completed";

    /// <summary>Metadata-only journal fact that a bounded collaboration envelope was recorded.</summary>
    public const string CollaborationMessageRecorded = "collaboration.message_recorded";

    /// <summary>Metadata-only fact: an <see cref="Artifact"/> was durably recorded for an
    /// attempt. The payload carries the artifact id, purpose, byte length, truncation flag, and
    /// capture outcome — never captured content.</summary>
    public const string ProcessOutputCaptured = "process.output_captured";

    /// <summary>Metadata-only fact: a Process attempt reached a terminal state with no artifact
    /// ever recorded — the adapter failed before a child process or output sink existed, so
    /// there was never anything to seal or capture. The payload carries only the attempt's
    /// final status; never exception text, arguments, environment values, or output.</summary>
    public const string ProcessEndedWithoutOutput = "process.ended_without_output";

    /// <summary>Metadata-only fact: a Codex planning Agent attempt was durably committed to
    /// being invoked. The payload carries only the attempt id and timestamp.</summary>
    public const string AgentAttemptDispatched = "agent.attempt_dispatched";

    /// <summary>Metadata-only fact: an Agent attempt reached a terminal state. The payload
    /// carries the attempt's final status and closed <see cref="AgentOutcome"/>; never raw
    /// provider output, exception text, or credentials.</summary>
    public const string AgentAttemptCompleted = "agent.attempt_completed";

    /// <summary>Metadata-only fact: a partial Agent-attempt output artifact left behind by a host
    /// interruption was imported by restart recovery. Distinct from
    /// <see cref="AgentAttemptCompleted"/> — the owning attempt is still <c>Running</c> when this
    /// is recorded, and recovering one artifact is never itself a terminal result.</summary>
    public const string AgentOutputRecovered = "agent.output_recovered";
}
