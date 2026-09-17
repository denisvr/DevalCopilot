namespace DevalCopilot.Domain.Features.EnvironmentReadiness;

/// <summary>
/// One durable, host-scoped fact per catalog <see cref="Capability"/> — never per
/// project. Registering any number of projects never creates another row: every project's
/// readiness view is a read-time projection of this single shared observation. Retains the
/// last successful evidence across a transient failure, so a probe that fails once never
/// erases what was last known to work.
/// </summary>
public sealed class HostCapabilitySnapshot
{
    private HostCapabilitySnapshot()
    {
    }

    public static HostCapabilitySnapshot Seed(Capability capability, DateTimeOffset nowUtc) => new()
    {
        Capability = capability,
        ReasonCode = CapabilityProbeReason.NeverProbed,
        NextProbeDueAtUtc = nowUtc,
    };

    public Capability Capability { get; private set; }

    /// <summary>The most recent probe's requirement-agnostic classification. <see
    /// cref="CapabilityProbeReason.None"/> means the last probe succeeded; any other value
    /// describes why it did not, independent of whether this capability is required.</summary>
    public CapabilityProbeReason ReasonCode { get; private set; }

    /// <summary>Path resolved by the last *successful* probe. Left untouched by a later
    /// failure — this is last-known-good evidence, not "current" evidence. For
    /// <see cref="CapabilityLaunchKind.NodeScript"/>, this is the direct, fully qualified
    /// <c>node.exe</c> path — the actual OS executable invoked — never the JavaScript
    /// entrypoint.</summary>
    public string? ResolvedExecutablePath { get; private set; }

    /// <summary>How the last *successful* probe launched <see cref="ResolvedExecutablePath"/>.
    /// Null only when no probe has ever succeeded. Left untouched by a later failure, for the
    /// same reason as <see cref="ResolvedExecutablePath"/>.</summary>
    public CapabilityLaunchKind? LaunchKind { get; private set; }

    /// <summary>The JavaScript entrypoint invoked as the first launch argument. Set only when
    /// <see cref="LaunchKind"/> is <see cref="CapabilityLaunchKind.NodeScript"/>; always null for
    /// <see cref="CapabilityLaunchKind.DirectExecutable"/>. Left untouched by a later failure,
    /// for the same reason as <see cref="ResolvedExecutablePath"/>.</summary>
    public string? ResolvedScriptPath { get; private set; }

    /// <summary>Version parsed by the last *successful* probe. Left untouched by a later
    /// failure, for the same reason as <see cref="ResolvedExecutablePath"/>.</summary>
    public string? ObservedVersion { get; private set; }

    /// <summary>When the last *successful* probe observed the evidence above.</summary>
    public DateTimeOffset? EvidenceObservedAtUtc { get; private set; }

    /// <summary>When this capability next becomes eligible for a probe.</summary>
    public DateTimeOffset NextProbeDueAtUtc { get; private set; }

    /// <summary>
    /// The durable in-flight marker for the current probe cycle: set before the discovery
    /// adapter is ever invoked, and cleared only when that cycle's result is recorded (success
    /// or failure) or a restart finds it stuck. Unlike a process attempt's dispatch marker,
    /// this exists for resource hygiene — avoiding two overlapping probes for the same
    /// capability — not because repeating a read-only version probe would be unsafe.
    /// </summary>
    public DateTimeOffset? ProbeDispatchedAtUtc { get; private set; }

    public void MarkDispatched(DateTimeOffset nowUtc)
    {
        if (ProbeDispatchedAtUtc.HasValue)
        {
            throw new InvalidOperationException("This capability was already marked dispatched.");
        }

        ProbeDispatchedAtUtc = nowUtc;
    }

    /// <summary>
    /// Records a successful probe: updates the last-known-good evidence, clears the dispatch
    /// marker, and schedules the next probe. This method is the durable owner of these paths and
    /// independently validates every component — it never relies on a caller (including
    /// <c>ToolDiscoveryResult</c>'s own factories) having behaved correctly, since a malformed
    /// value that reaches here would otherwise be persisted as fact. Every check below runs
    /// before <see cref="RequireDispatched"/> and before any field is mutated.
    /// </summary>
    /// <param name="resolvedExecutablePath">Must be a non-blank, fully qualified path.</param>
    /// <param name="resolvedScriptPath">Required, non-blank, and fully qualified when
    /// <paramref name="launchKind"/> is <see cref="CapabilityLaunchKind.NodeScript"/>; must be
    /// null for <see cref="CapabilityLaunchKind.DirectExecutable"/> — enforced here so an invalid
    /// launch combination can never reach durable storage.</param>
    public void RecordSuccess(
        CapabilityLaunchKind launchKind,
        string resolvedExecutablePath,
        string? resolvedScriptPath,
        string observedVersion,
        DateTimeOffset nowUtc,
        DateTimeOffset nextProbeDueAtUtc)
    {
        if (!Enum.IsDefined(launchKind))
        {
            throw new ArgumentOutOfRangeException(nameof(launchKind), launchKind, "Not a defined launch kind.");
        }

        RequireAbsolutePath(resolvedExecutablePath, nameof(resolvedExecutablePath));
        ArgumentException.ThrowIfNullOrWhiteSpace(observedVersion);

        switch (launchKind)
        {
            case CapabilityLaunchKind.NodeScript:
                RequireAbsolutePath(resolvedScriptPath, nameof(resolvedScriptPath));
                break;
            case CapabilityLaunchKind.DirectExecutable when resolvedScriptPath is not null:
                throw new ArgumentException(
                    "A direct-executable launch never carries a script path.", nameof(resolvedScriptPath));
        }

        RequireDispatched();

        ReasonCode = CapabilityProbeReason.None;
        ResolvedExecutablePath = resolvedExecutablePath;
        LaunchKind = launchKind;
        ResolvedScriptPath = resolvedScriptPath;
        ObservedVersion = observedVersion;
        EvidenceObservedAtUtc = nowUtc;
        ProbeDispatchedAtUtc = null;
        NextProbeDueAtUtc = nextProbeDueAtUtc;
    }

    private static void RequireAbsolutePath(string? path, string paramName)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException($"'{paramName}' must be a non-blank, absolute path.", paramName);
        }
    }

    /// <summary>
    /// Records a failed probe: only the reason and schedule change. Last-known-good evidence
    /// is intentionally left untouched, so a transient failure never erases it.
    /// </summary>
    public void RecordFailure(CapabilityProbeReason reason, DateTimeOffset nextProbeDueAtUtc)
    {
        if (reason is CapabilityProbeReason.None or CapabilityProbeReason.NeverProbed
            or CapabilityProbeReason.ProbeInterruptedByRestart)
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "Not a valid probe-failure reason.");
        }

        RequireDispatched();

        ReasonCode = reason;
        ProbeDispatchedAtUtc = null;
        NextProbeDueAtUtc = nextProbeDueAtUtc;
    }

    /// <summary>
    /// The restart-reconciliation transition: clears a dispatch marker left stuck by a crash or
    /// stalled recording, and makes this capability immediately eligible again. A no-op if
    /// nothing is stuck — safe to call unconditionally at startup.
    /// </summary>
    public void ClearStuckDispatch(DateTimeOffset nowUtc)
    {
        if (!ProbeDispatchedAtUtc.HasValue)
        {
            return;
        }

        ReasonCode = CapabilityProbeReason.ProbeInterruptedByRestart;
        ProbeDispatchedAtUtc = null;
        NextProbeDueAtUtc = nowUtc;
    }

    /// <summary>
    /// The manual "Refresh now" transition: pulls the next probe forward to now. A no-op while
    /// a probe is already in flight — refreshing does not queue a second one.
    /// </summary>
    public void PullProbeDueForward(DateTimeOffset nowUtc)
    {
        if (ProbeDispatchedAtUtc.HasValue)
        {
            return;
        }

        if (nowUtc < NextProbeDueAtUtc)
        {
            NextProbeDueAtUtc = nowUtc;
        }
    }

    private void RequireDispatched()
    {
        if (!ProbeDispatchedAtUtc.HasValue)
        {
            throw new InvalidOperationException("Cannot record a probe result before it was marked dispatched.");
        }
    }
}
