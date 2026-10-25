namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;

/// <summary>
/// ADR-0065: the feature-owned identity of one host launch, created once when the host process starts and held only in memory. A dispatch
/// witness records the launch identity that committed its dispatch marker, so provenance names the original host. It is deliberately not
/// <c>AgentSupervision</c> and never an authority: no client, provider reply or environment variable supplies it, it is never exposed,
/// and a new launch (a restart) is a different identity that holds no ticket of an earlier one.
/// </summary>
public sealed class LocalExecutionHostLaunch
{
    /// <summary>A new, distinct launch identity.</summary>
    public LocalExecutionHostLaunch()
    {
        Id = Guid.NewGuid();
    }

    /// <summary>The identity of the process-wide launch a handler uses when none was composed explicitly. Distinct for every process.</summary>
    public static LocalExecutionHostLaunch ProcessWide { get; } = new();

    internal Guid Id { get; }
}
