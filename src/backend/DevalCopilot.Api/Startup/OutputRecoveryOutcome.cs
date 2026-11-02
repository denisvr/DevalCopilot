namespace DevalCopilot.Api.Startup;

/// <summary>
/// ADR-0072: the typed per-launch outcome of one historical output recovery pass. Both flags are optional-output conditions only; a
/// database fault is never folded into them and escapes the pass as fatal.
/// </summary>
public sealed record OutputRecoveryOutcome(bool OptionalOutputDeferred, bool RedundantCleanupDeferred)
{
    public static OutputRecoveryOutcome None { get; } = new(false, false);
}
