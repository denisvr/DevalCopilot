namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// How a local execution sequence reaches its operations (ADR-0062). The string values are durable schema. A sequence recorded before
/// the queue existed is <see cref="Immediate"/>; the mode is immutable and never adopted from one mode to the other.
/// </summary>
public enum LocalExecutionAdmissionMode
{
    /// <summary>ADR-0060: the coordinator reserves the next step directly and a failed admission ends the cycle.</summary>
    Immediate = 0,

    /// <summary>ADR-0062: every step first waits as a pending admission and is reserved only when the workspace and capacity allow.</summary>
    WaitForAdmission = 1,
}
