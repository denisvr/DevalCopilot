using DevalCopilot.Application.Features.Runs.Ports;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>A strict-observation double a hosted test registers once and then scripts per scenario: the answer is a function of the
/// zero-based call index, so the claim-time and dispatch-time observations of one attempt can differ deterministically. It records every
/// call; it never touches a process or a provider.</summary>
public sealed class ScriptedAccountUsageAdapter : IAccountUsageObserver
{
    private int _calls;

    public Func<int, AccountUsageObservation> Respond { get; set; } = _ => AccountUsageObservation.Unavailable;

    public int Calls => Volatile.Read(ref _calls);

    public Task<AccountUsageObservation> ObserveAsync(string executablePath, string? scriptPath, CancellationToken cancellationToken) =>
        Task.FromResult(Respond(Interlocked.Increment(ref _calls) - 1));
}
