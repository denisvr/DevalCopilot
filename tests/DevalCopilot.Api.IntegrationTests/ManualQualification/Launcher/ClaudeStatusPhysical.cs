namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>The physical outcome of one status child, kept separate from what it reported. A logged-out report with the documented
/// exit code is a completed (<see cref="Exited"/>) observation, never a physical failure.</summary>
public enum ClaudeStatusPhysical
{
    NotStarted = 0,
    Refused = 1,
    Exited = 2,
    TimedOut = 3,
    Cancelled = 4,
    StartFailed = 5,

    /// <summary>The start was admitted (its slot was consumed and its request reached the process boundary) but the entry's bound
    /// elapsed before the outcome was observed: unproven, never reported as not started.</summary>
    StartedUnproven = 6,
}
