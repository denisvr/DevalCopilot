namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The closed phases of one host observation of an Agent attempt (ADR-0056). A phase says only which stage of its own supervision the
/// host observer last reported: never provider progress, a started child process or useful work. The string values are durable schema.
/// Phases only move forward, each at most once.
/// </summary>
public enum AgentSupervisionPhase
{
    /// <summary>The supervisor entered its per-attempt execution: Git and account preparation and the dispatch gate. No adapter call yet.</summary>
    Preparing = 0,

    /// <summary>The ordinary dispatch gate succeeded and the existing adapter call is about to start. It does not claim a process started.</summary>
    AwaitingAdapterResult = 1,

    /// <summary>The adapter call returned or threw, or none was made; the existing post-source, artifact and result-recording work runs.</summary>
    RecordingResult = 2,
}
