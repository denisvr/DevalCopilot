namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The immutable execution contract a Run was created under. It is persisted as its numeric value
/// so a stored number outside this set is representable and is never treated as recognized.
/// </summary>
public enum RunExecutionMode
{
    /// <summary>The mode was not recorded. Every Run that predates this decision carries this
    /// value; it is never inferred from attempts, providers, lifecycle, or events, and it keeps
    /// the historical admission behavior (legacy simulation, Agent stages, and Process support).
    /// No creation operation assigns it.</summary>
    Legacy = 0,

    /// <summary>A deliberately created walking-skeleton demonstration: only the deterministic
    /// simulation may execute it. It never admits an Agent or Process attempt.</summary>
    Simulated = 1,

    /// <summary>A user-written objective awaiting explicitly requested collaboration stages:
    /// only the existing Agent claim paths may execute it. It never admits simulation or Process.</summary>
    ManualAgent = 2,
}
