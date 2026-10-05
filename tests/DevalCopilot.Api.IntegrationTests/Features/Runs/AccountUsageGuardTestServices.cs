using DevalCopilot.Api.HostedServices;
using DevalCopilot.Application.Features.Runs.Ports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>The account-usage stop (ADR-0025) collaborators of a hand-built hosted-test service provider: the same shared dispatch guard
/// <c>Program</c> registers, over an observation adapter that fails the test if it is ever asked, unless a test supplies its own. A run
/// with no stop configured must never cause an observation, so every pre-existing hosted test proves exactly that.</summary>
public static class AccountUsageGuardTestServices
{
    public static void Register(IServiceCollection services, IAccountUsageObserver? adapter = null)
    {
        services.RemoveAll<IAccountUsageObserver>();
        services.AddSingleton(adapter ?? new UnexpectedObservationAdapter());
        services.TryAddSingleton<CodexAccountUsageDispatchGuard>();
    }

    private sealed class UnexpectedObservationAdapter : IAccountUsageObserver
    {
        public Task<AccountUsageObservation> ObserveAsync(string executablePath, string? scriptPath, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No account-usage observation was expected: the run has no account-usage stop configured.");
    }
}
