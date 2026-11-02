namespace DevalCopilot.Api.Startup;

/// <summary>
/// ADR-0072: the one closed diagnosis of a fatal startup recovery failure. It carries only these three closed values: never a path,
/// exception, message, database payload, environment value or secret. <see cref="Code"/> is the copyable safe diagnostic the shell
/// and the direct-host stderr line both show.
/// </summary>
public sealed record StartupFailure(StartupFailureStage Stage, StartupFailureReason Reason)
{
    /// <summary>Storage that could not be opened, written or reached is restorable by the person; anything else needs reviewed repair.</summary>
    public StartupFailureAction Action => Reason == StartupFailureReason.StorageUnavailable
        ? StartupFailureAction.RestoreStorageAccess
        : StartupFailureAction.RequestReviewedRepair;

    public string Code => $"DCSF1-{Stage}-{Reason}-{Action}";
}
