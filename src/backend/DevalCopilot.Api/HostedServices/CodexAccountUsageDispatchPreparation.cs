using DevalCopilot.Application.Features.Runs.Policies;

namespace DevalCopilot.Api.HostedServices;

/// <summary>The dispatch guard's answer for one attempt: stopped (already resolved or blocked, nothing more to do), or proceed with the
/// facts to pass to the dispatch gate (null when the attempt claimed no stop).</summary>
public sealed record CodexAccountUsageDispatchPreparation(bool Stopped, CodexAccountUsageGuardFacts? Facts);
