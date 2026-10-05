using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>A controllable strict-observation double for the account-usage stop (ADR-0025): it records every call with the exact launch
/// tuple it was asked about, and answers from a per-call function so a test can return different evidence at the claim and at the
/// dispatch seam. It never touches a process or a provider.</summary>
public sealed class StubAccountUsageAdapter : IAccountUsageObserver
{
    private readonly Func<int, string, string?, CancellationToken, Task<AccountUsageObservation>> _respond;
    private int _calls;

    public StubAccountUsageAdapter(Func<int, AccountUsageObservation> respond)
        : this((call, _, _, _) => Task.FromResult(respond(call)))
    {
    }

    public StubAccountUsageAdapter(Func<int, string, string?, CancellationToken, Task<AccountUsageObservation>> respond) => _respond = respond;

    public static StubAccountUsageAdapter Always(AccountUsageObservation observation) => new(_ => observation);

    public int Calls => Volatile.Read(ref _calls);

    public List<(string Executable, string? Script)> Tuples { get; } = [];

    public Task<AccountUsageObservation> ObserveAsync(string executablePath, string? scriptPath, CancellationToken cancellationToken)
    {
        var call = Interlocked.Increment(ref _calls) - 1;
        Tuples.Add((executablePath, scriptPath));
        return _respond(call, executablePath, scriptPath, cancellationToken);
    }
}
