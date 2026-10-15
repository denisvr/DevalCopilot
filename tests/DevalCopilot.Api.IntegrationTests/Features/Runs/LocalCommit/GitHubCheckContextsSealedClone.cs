using System.Collections.Concurrent;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Queries.GetGitHubCheckContextsObservation;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// One fact's private copy of a sealed stage: its own physical SQLite file made by the backup facility, its own writable root, and a
/// fresh production host with its own scripted observers, counters and clock. Only the sealed scene is shared, read-only. The owner
/// disposes the clone, which stops its host before it removes the files this clone created.
/// </summary>
internal sealed class GitHubCheckContextsSealedClone : IDisposable
{
    private readonly GitHubCheckContextsSealedEvidence _evidence;
    private readonly string _directory;
    private LocalCommitHost? _host;
    private bool _disposed;

    internal GitHubCheckContextsSealedClone(GitHubCheckContextsSealedEvidence evidence, string directory)
    {
        _evidence = evidence;
        _directory = directory;
        DatabasePath = Path.Combine(directory, "evidence.db");
        WritableRoot = Path.Combine(directory, "writable");
    }

    public string DatabasePath { get; }

    public string WritableRoot { get; }

    /// <summary>The calls this clone's host refused because they would have written to, or invoked a provider about, the sealed scene.</summary>
    public ConcurrentQueue<string> Refusals { get; } = new();

    public LocalCommitHost? Host => _host;

    /// <summary>Copies the stage into this clone's file, records current CLI evidence in it (never a cached verdict) and starts the clone's
    /// production host with observers, counters and a clock of its own. A guard regression may pass <paramref name="processExecution"/> to
    /// observe process starts from the host; no existing fact passes one.</summary>
    public async Task<GitHubCheckContextsObservationTestBase.Bound> ProvisionAsync(
        GitHubCheckContextsSealedStage stage,
        IGitHubCheckContextsObserver? observer = null,
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

        var identityObserver = new GitHubRepositoryIdentityBindingTestBase.ScriptedObserver
        {
            Answer = () => GitHubRepositoryIdentityBindingTestBase.Identity(),
        };
        var contexts = new GitHubCheckContextsObservationTestBase.ScriptedContextsObserver();
        var clock = new GitHubCheckContextsObservationTestBase.MovableClock();
        _host = new LocalCommitHost(
            _evidence.Scene,
            DatabasePath,
            runSupervisor: false,
            configure: services =>
            {
                services.RemoveAll<IGitHubRepositoryIdentityObserver>();
                services.AddSingleton<IGitHubRepositoryIdentityObserver>(identityObserver);
                services.RemoveAll<IGitHubCheckContextsObserver>();
                services.AddSingleton(observer ?? contexts);
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock);
                SealedSceneGuard.Apply(services, Refusals);
                if (processExecution is not null)
                {
                    services.RemoveAll<IProcessExecutionAdapter>();
                    services.AddSingleton(processExecution);
                }
            },
            writableRoot: WritableRoot);
        _ = _host.Services;

        var identities = _evidence.Identities;
        return new GitHubCheckContextsObservationTestBase.Bound(
            _host, identityObserver, contexts, clock, identities.IdsForClone(), identities.OperationId, identities.Sha, identities.TargetId);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _host?.Dispose();
        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={DatabasePath}"));
        SealedFileTree.DeleteOwned(_directory);
    }
}
