using System.Collections.Concurrent;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Application.Features.Runs.Queries.GetGitHubCheckContextsObservation;
using DevalCopilot.Application.Features.Runs.Queries.GetGitHubCheckRunOutputObservation;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// The ADR-0047 endpoint class's own fact-scoped clone (feature-owned composition of the shipped ADR-0046 sealed producer, unchanged):
/// it borrows one directory, one physical SQLite copy made by the producer's backup facility and one writable root from a reservation of
/// the class's own evidence instance, then starts a fresh production host with observers, counters and a clock that belong to this clone
/// alone. The sealed scene stays read-only: the shared guard refuses every provider, the GitHub observers it lists and every Git writer;
/// this feature-owned composition additionally refuses the repository identity and check-context observers (which the shared guard
/// leaves to each class), with the same mechanism and named refusals in this clone's own queue, and enables only this operation's
/// controlled output observer, after the guard. Disposal stops the host before the reservation removes the files.
/// </summary>
internal sealed class GitHubCheckRunOutputSealedClone : IDisposable
{
    /// <summary>GitHub observers unrelated to this operation that the shared guard does not list; this composition refuses them too.</summary>
    internal static readonly Type[] UnrelatedObservers = [typeof(IGitHubRepositoryIdentityObserver), typeof(IGitHubCheckContextsObserver)];

    private readonly GitHubCheckContextsSealedEvidence _evidence;
    private readonly GitHubCheckContextsSealedClone _reservation;
    private LocalCommitHost? _host;
    private bool _disposed;

    internal GitHubCheckRunOutputSealedClone(GitHubCheckContextsSealedEvidence evidence)
    {
        _evidence = evidence;
        _reservation = evidence.ReserveClone();
    }

    public string DatabasePath => _reservation.DatabasePath;

    public string WritableRoot => _reservation.WritableRoot;

    /// <summary>The calls this clone's host refused because they would have written to, or invoked a provider about, the sealed scene.</summary>
    public ConcurrentQueue<string> Refusals { get; } = new();

    public LocalCommitHost? Host => _host;

    /// <summary>Copies the stage into this clone's file, records current CLI evidence in it (never a cached verdict) and starts the clone's
    /// production host. Only this operation's own observer is enabled, after the shared guard and this composition have refused every
    /// unrelated observer. A guard regression may pass <paramref name="processExecution"/> to observe process starts from the host.</summary>
    public async Task<GitHubCheckRunOutputObservationTestBase.Bound> ProvisionAsync(
        GitHubCheckContextsSealedStage stage,
        IGitHubCheckRunOutputObserver? observer = null,
        IProcessExecutionAdapter? processExecution = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_host is not null)
        {
            throw new InvalidOperationException("A clone is provisioned once; reserve another for another fact.");
        }

        _evidence.CopySealedStageTo(stage, DatabasePath);
        await using (var db = new DevalCopilotDbContext(
                         new DbContextOptionsBuilder<DevalCopilotDbContext>().UseSqlite($"Data Source={DatabasePath}").Options))
        {
            await GitHubRepositoryIdentityBindingTestBase.SeedSnapshotAsync(db);
        }

        var output = new GitHubCheckRunOutputObservationTestBase.ScriptedOutputObserver();
        var clock = new GitHubCheckRunOutputObservationTestBase.OutputClock();
        _host = new LocalCommitHost(
            _evidence.Scene,
            DatabasePath,
            runSupervisor: false,
            configure: services =>
            {
                SealedSceneGuard.Apply(services, Refusals);
                foreach (var port in UnrelatedObservers)
                {
                    services.RemoveAll(port);
                    services.AddSingleton(port, _ => SealedSceneGuard.RefusingProxy.Wrap(port, Refusals, null, []));
                }

                services.RemoveAll<IGitHubCheckRunOutputObserver>();
                services.AddSingleton(observer ?? output);
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock);
                if (processExecution is not null)
                {
                    services.RemoveAll<IProcessExecutionAdapter>();
                    services.AddSingleton(processExecution);
                }
            },
            writableRoot: WritableRoot);
        _ = _host.Services;

        var identities = _evidence.Identities;
        return new GitHubCheckRunOutputObservationTestBase.Bound(
            _host, null, output, clock, identities.IdsForClone(), identities.OperationId, identities.Sha, identities.TargetId);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _host?.Dispose();
        _reservation.Dispose();
    }
}
