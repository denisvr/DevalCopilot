namespace DevalCopilot.Application.Data;

/// <summary>The three-way outcome of an <see cref="ILocalExecutionStepProbe"/> check. <see cref="Unresolved"/> is not "probably absent":
/// a caller treats it as unknown: no cleanup of possibly referenced artifacts, no success claim, no retry.</summary>
public enum LocalExecutionStepProbeResult
{
    /// <summary>The step is linked to its reserved operation identity and that exact operation row exists.</summary>
    Claimed,

    /// <summary>The step is not linked and no operation row with its reserved identity exists.</summary>
    NotClaimed,

    Unresolved,
}
