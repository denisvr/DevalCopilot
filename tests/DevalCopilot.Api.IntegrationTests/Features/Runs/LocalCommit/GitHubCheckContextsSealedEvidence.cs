using System.Net;
using DevalCopilot.Application.Features.Runs.Ports;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// The class-owned sealed evidence of ADR-0046, created once per execution of the class that uses it and cached nowhere else. It runs
/// the unchanged production lineage (real delivery, real Git local commit, protected observation-target POST and identity-binding
/// POST) in one real Git scene, stops every seed host, and seals two images of that one lineage with SQLite's backup facility: the
/// state with the target recorded and the state with the binding recorded. Sealed files are never opened for writing again; every
/// fact receives its own physical copy through <see cref="ReserveClone"/>. The scene stays read-only evidence that this fixture owns
/// and disposes after every fact has finished, and its fingerprint must be unchanged then.
/// </summary>
public sealed class GitHubCheckContextsSealedEvidence : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"devalcopilot-check-contexts-sealed-{Guid.NewGuid():N}");
    private readonly Dictionary<GitHubCheckContextsSealedStage, string> _paths = [];
    private readonly Dictionary<GitHubCheckContextsSealedStage, string> _hashes = [];
    private SeedLineage? _seed;
    private string _sceneFingerprint = string.Empty;

    internal LocalCommitScene Scene => _seed!.Scene;

    internal GitHubCheckContextsSealedIdentities Identities { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(_root, "clones"));
        try
        {
            _seed = new SeedLineage();

            // The unchanged production lineage up to the recorded target, on a host the seed then stops before anything is sealed.
            var delivered = await _seed.BoundAsync(recordBinding: false);
            Identities = GitHubCheckContextsSealedIdentities.Capture(delivered.Ids, delivered.OperationId, delivered.Sha, delivered.TargetId);
            delivered.Host.Dispose();
            Seal(GitHubCheckContextsSealedStage.TargetRecorded);

            // The production identity-binding POST over the same database, on another stopped-before-sealing host.
            var identityObserver = new GitHubRepositoryIdentityBindingTestBase.ScriptedObserver
            {
                Answer = () => GitHubRepositoryIdentityBindingTestBase.Identity(),
            };
            using (var host = _seed.StartHost(
                       runSupervisor: false,
                       configure: services =>
                       {
                           services.RemoveAll<IGitHubRepositoryIdentityObserver>();
                           services.AddSingleton<IGitHubRepositoryIdentityObserver>(identityObserver);
                       }))
            {
                var body = GitHubRepositoryIdentityBindingTestBase.BindingBodyOf(
                    Identities.OperationId, Identities.Sha, Identities.TargetId);
                var posted = await GitHubRepositoryIdentityBindingTestBase.PostBindingAsync(host, Identities.Ids.RunId, body);
                Assert.Equal(HttpStatusCode.OK, posted.Status);
            }

            Seal(GitHubCheckContextsSealedStage.BindingRecorded);
            _sceneFingerprint = SealedFileTree.Fingerprint(Scene.Root);
        }
        catch
        {
            Release();
            throw;
        }
    }

    public Task DisposeAsync()
    {
        try
        {
            if (_seed is not null)
            {
                AssertSealedUnchanged();
            }
        }
        finally
        {
            Release();
        }

        return Task.CompletedTask;
    }

    /// <summary>Reserves one fact's private clone: a directory of its own for its database and its writable roots. Nothing is copied
    /// until the clone is provisioned.</summary>
    internal GitHubCheckContextsSealedClone ReserveClone()
    {
        var directory = Path.Combine(_root, "clones", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "writable"));
        return new GitHubCheckContextsSealedClone(this, directory);
    }

    /// <summary>Copies a sealed stage into a new file with SQLite's consistent backup facility. The sealed file is opened read-only.</summary>
    internal void CopySealedStageTo(GitHubCheckContextsSealedStage stage, string destinationPath)
    {
        using var source = new SqliteConnection($"Data Source={_paths[stage]};Mode=ReadOnly;Pooling=False");
        using var destination = new SqliteConnection($"Data Source={destinationPath};Pooling=False");
        source.Open();
        destination.Open();
        source.BackupDatabase(destination);
    }

    internal string SealedPath(GitHubCheckContextsSealedStage stage) => _paths[stage];

    internal string SealedHash(GitHubCheckContextsSealedStage stage) => _hashes[stage];

    internal string SceneFingerprint() => SealedFileTree.Fingerprint(Scene.Root);

    /// <summary>Proves the sealed files and the read-only scene are byte-for-byte what they were when sealed.</summary>
    internal void AssertSealedUnchanged()
    {
        foreach (var (stage, hash) in _hashes)
        {
            Assert.True(hash == SealedFileTree.FileHash(_paths[stage]), $"the sealed {stage} database changed after it was sealed");
        }

        Assert.True(_sceneFingerprint == SceneFingerprint(), "the sealed Git scene changed after it was sealed");
    }

    private void Seal(GitHubCheckContextsSealedStage stage)
    {
        var seedDatabase = _seed!.DatabasePath;
        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={seedDatabase}"));
        foreach (var sidecar in new[] { "-wal", "-shm", "-journal" })
        {
            Assert.False(File.Exists(seedDatabase + sidecar), $"the stopped seed database still has a {sidecar} file");
        }

        var path = Path.Combine(_root, $"sealed-{stage}.db");
        using (var source = new SqliteConnection($"Data Source={seedDatabase};Mode=ReadOnly;Pooling=False"))
        using (var destination = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            source.Open();
            destination.Open();
            source.BackupDatabase(destination);
        }

        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={path}"));
        File.SetAttributes(path, FileAttributes.ReadOnly);
        _paths[stage] = path;
        _hashes[stage] = SealedFileTree.FileHash(path);
    }

    private void Release()
    {
        _seed?.Dispose();
        _seed = null;
        SealedFileTree.DeleteOwned(_root);
    }

    /// <summary>The seed's own test-instance plumbing: its scene and database are the ones that become sealed evidence.</summary>
    private sealed class SeedLineage : GitHubCheckContextsObservationTestBase
    {
    }
}
