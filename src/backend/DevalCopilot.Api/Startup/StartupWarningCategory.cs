namespace DevalCopilot.Api.Startup;

/// <summary>
/// ADR-0072: a bounded nonfatal startup condition, reported only when every database transaction and safety-critical reconciliation
/// was proven. A warning creates no evidence, readiness or release and never bypasses a hold.
/// </summary>
public enum StartupWarningCategory
{
    /// <summary>A historical output of an interrupted attempt could not be sealed this launch; its partial file is preserved.</summary>
    OptionalOutputDeferred = 0,

    /// <summary>A proved-redundant partial output could not be removed this launch; it is left in place.</summary>
    RedundantCleanupDeferred = 1,

    /// <summary>Some supervision observations of a previous launch could not be closed; they stay as recorded.</summary>
    SupervisionObservationsUnclosed = 2,
}
