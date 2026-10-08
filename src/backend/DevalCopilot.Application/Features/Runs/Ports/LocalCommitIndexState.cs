namespace DevalCopilot.Application.Features.Runs.Ports;

public enum LocalCommitIndexState
{
    /// <summary>Byte-identical to the recorded preimage.</summary>
    Preimage,

    /// <summary>Byte-identical to the recorded prepared index.</summary>
    Prepared,

    Other,
    Missing,
}
