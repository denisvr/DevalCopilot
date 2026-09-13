namespace DevalCopilot.Domain.Features.EnvironmentReadiness;

/// <summary>
/// A closed, requirement-agnostic fact about one capability's last probe on this host. Never
/// encodes "required vs. optional" — that classification is applied only at projection time,
/// once per consuming project, on top of this shared host fact.
/// </summary>
public enum CapabilityProbeReason
{
    /// <summary>Seeded, never yet probed. A bootstrap/loading presentation, not a display
    /// status.</summary>
    NeverProbed = 0,

    /// <summary>The most recent probe found the executable and parsed a version.</summary>
    None = 1,

    /// <summary>Not found in any normalized host PATH segment or fixed fallback directory.</summary>
    ExecutableNotFound = 2,

    /// <summary>Found, but starting it failed (for example, a permission or launch failure).
    /// Distinct from <see cref="ExecutableNotFound"/>: the path genuinely exists.</summary>
    ExecutableInaccessible = 3,

    /// <summary>Found and started, but did not exit within the bounded probe timeout.</summary>
    ProbeTimedOut = 4,

    /// <summary>Ran and exited, but its captured output did not match the expected version
    /// shape. The raw output is never retained.</summary>
    VersionProbeUnparseable = 5,

    /// <summary>An application restart found this capability's dispatch marker still set with
    /// no recorded result — a stalled or crashed probe, not a terminal failure. Cleared and
    /// immediately re-eligible, never retried automatically within the same host instance.</summary>
    ProbeInterruptedByRestart = 6,
}
