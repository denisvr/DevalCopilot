namespace DevalCopilot.Domain.Features.Runs;

/// <summary>What an Agent attempt's own immutable record says about a requested Claude agentic-turn
/// limit. It is a statement about recorded provenance only, never an observed turn count or an
/// observed unlimited capacity.</summary>
public enum ClaudeMutationTurnLimitEvidence
{
    /// <summary>The attempt predates the request (a known v1 mutation contract) or is not a Claude
    /// mutation attempt at all: no request was recorded, so nothing is claimed either way.</summary>
    NotRecorded = 0,

    /// <summary>A coherent v2 mutation attempt that recorded no request: there is nothing saved to pass as a limit.</summary>
    NotRequested = 1,

    /// <summary>A coherent v2 mutation attempt whose immutable snapshot records this request (or, for a Run, a saved    /// valid request). This states the saved or snapshotted request only, including for an attempt that has not been    /// dispatched yet and a Run that has no attempt yet; what the versioned adapter then does with a recorded request is    /// a separate fact of that adapter contract and is never inferred from this one.</summary>
    Requested = 2,

    /// <summary>The recorded facts do not agree (a limit beside a contract that cannot carry it, an
    /// out-of-range value, or an unrecognized contract version): nothing is claimed.</summary>
    Unknown = 3,
}
