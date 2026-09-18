namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// What kind of work an <see cref="Attempt"/> represents. <see cref="Simulated"/> is the
/// migration default so every attempt recorded before this kind existed is classified
/// correctly without a data backfill.
/// </summary>
public enum AttemptKind
{
    Simulated = 0,
    Process = 1,

    /// <summary>A durable, real invocation of an external agent provider — Codex planning in this
    /// slice. Distinct from <see cref="Process"/>: an Agent attempt's committed intent describes
    /// a provider/role/protocol contract and a Git checkpoint it is evidence about, never a bare
    /// executable/arguments/working-directory triple.</summary>
    Agent = 2,
}
