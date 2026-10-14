using System.Net;
using System.Reflection;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Application.Features.Runs.Queries.GetGitHubCheckContextsObservation;
using DevalCopilot.Infrastructure.Features.Runs;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using static DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit.GitHubCheckContextsObservationTestBase;
using static DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit.GitHubRepositoryIdentityBindingTestBase;
using static DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit.LocalDeliveryReceiptTestBase;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// ADR-0046: the class-owned sealed snapshot gives every fact a private copy. Two clones live at once never share a database, a
/// writable root, an observer, a counter or a clock; a change, a failure or a disposal in one never reaches the other, a later clone
/// or the sealed source; the seed really executed the production lineage; and an observation host cannot run a supervisor, a provider
/// or a Git writer against the sealed scene.
/// </summary>
public sealed class GitHubCheckContextsSealedEvidenceIsolationTests(GitHubCheckContextsSealedEvidence evidence)
    : IClassFixture<GitHubCheckContextsSealedEvidence>, IDisposable
{
    private const string Snapshots = "host_capability_snapshots";

    private readonly List<GitHubCheckContextsSealedClone> _clones = [];

    public void Dispose()
    {
        foreach (var clone in _clones)
        {
            clone.Dispose();
        }
    }

    private async Task<(GitHubCheckContextsSealedClone Clone, Bound Bound)> LiveAsync(
        GitHubCheckContextsSealedStage stage = GitHubCheckContextsSealedStage.BindingRecorded)
    {
        var clone = evidence.ReserveClone();
        _clones.Add(clone);
        return (clone, await clone.ProvisionAsync(stage));
    }

    private Func<Task<GitHubCheckContextsProbe?>> Matching(Bound bound, string name) => () => ContextsProbe(
        "OCTO-CAT",
        "Hello.World",
        reference: new GitHubCheckContextsRef(evidence.Scene.BranchName, bound.Sha),
        page: PageOf(1, RunEntry(name: name)));

    /// <summary>Applies statements to a clone's file the way a corrupting test does: the triggers of the touched tables are dropped
    /// first, and nothing is restored because the clone is discarded.</summary>
    private static async Task ChangeAsync(GitHubCheckContextsSealedClone clone, string[] tables, params string[] statements)
    {
        await using var connection = new SqliteConnection($"Data Source={clone.DatabasePath};Pooling=False");
        await connection.OpenAsync();
        var triggers = new List<string>();
        foreach (var table in tables)
        {
            await using var read = connection.CreateCommand();
            read.CommandText = "SELECT name FROM sqlite_master WHERE type = 'trigger' AND tbl_name = $table";
            read.Parameters.AddWithValue("$table", table);
            await using var reader = await read.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                triggers.Add(reader.GetString(0));
            }
        }

        foreach (var statement in triggers.Select(name => $"DROP TRIGGER \"{name}\"").Concat(statements))
        {
            await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // Test-only SQL assembled from constants and trigger names read from sqlite_master.
            command.CommandText = statement;
#pragma warning restore CA2100
            await command.ExecuteNonQueryAsync();
        }
    }

    private static Task<string> FingerprintAsync(GitHubCheckContextsSealedClone clone) =>
        DatabaseFingerprintAsync(clone.DatabasePath);

    private void AssertSealedSourceUnchanged() => evidence.AssertSealedUnchanged();

    [Fact]
    public async Task Two_simultaneously_live_clones_have_distinct_databases_and_writable_resources()
    {
        var (first, firstBound) = await LiveAsync();
        var (second, secondBound) = await LiveAsync();
        var sealedPaths = new[]
        {
            evidence.SealedPath(GitHubCheckContextsSealedStage.TargetRecorded),
            evidence.SealedPath(GitHubCheckContextsSealedStage.BindingRecorded),
        };

        Assert.NotEqual(first.DatabasePath, second.DatabasePath);
        Assert.NotEqual(first.WritableRoot, second.WritableRoot);
        Assert.DoesNotContain(first.DatabasePath, sealedPaths);
        Assert.DoesNotContain(second.DatabasePath, sealedPaths);
        Assert.True(File.Exists(first.DatabasePath) && File.Exists(second.DatabasePath));
        Assert.NotSame(firstBound.Host, secondBound.Host);
        foreach (var (clone, bound) in new[] { (first, firstBound), (second, secondBound) })
        {
            var storage = bound.Host.Services.GetRequiredService<LocalCommitStorage>().Root;
            var workspaces = bound.Host.Services.GetRequiredService<IWorkspaceRootPathProvider>().ComputeWorkspacePath(Guid.Empty, 1);
            Assert.StartsWith(clone.WritableRoot, storage, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(clone.WritableRoot, workspaces, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(evidence.Scene.Root, storage, StringComparison.OrdinalIgnoreCase);
        }

        firstBound.Contexts.Answer = Matching(firstBound, "first");
        secondBound.Contexts.Answer = Matching(secondBound, "second");
        var firstReading = await ObserveContextsAsync(firstBound.Host, firstBound.Ids.RunId);
        var secondReading = await ObserveContextsAsync(secondBound.Host, secondBound.Ids.RunId);

        Assert.Equal("ContextsReported", StatusOf(firstReading));
        Assert.Equal("ContextsReported", StatusOf(secondReading));
        Assert.Equal("first", firstReading.Root.GetProperty("observation").GetProperty("contexts")[0].GetProperty("name").GetString());
        Assert.Equal("second", secondReading.Root.GetProperty("observation").GetProperty("contexts")[0].GetProperty("name").GetString());
        AssertSealedSourceUnchanged();
    }

    [Fact]
    public async Task Capability_binding_and_event_changes_in_one_clone_leave_the_other_clone_and_the_sealed_source_unchanged()
    {
        var (first, firstBound) = await LiveAsync();
        var (second, secondBound) = await LiveAsync();
        var runId = firstBound.Ids.RunId;
        var firstBefore = await FingerprintAsync(first);
        var secondBefore = await FingerprintAsync(second);
        Assert.Equal(
            await DatabaseFingerprintAsync(first.DatabasePath, [Snapshots]), await DatabaseFingerprintAsync(second.DatabasePath, [Snapshots]));

        await ChangeAsync(
            first,
            [Snapshots, "github_repository_identity_bindings", "events"],
            $"DELETE FROM {Snapshots} WHERE \"Capability\" = 'GitHubCli'",
            "UPDATE github_repository_identity_bindings SET \"Owner\" = 'someone-else'",
            $"DELETE FROM events WHERE {EventOfBinding(runId)}");

        Assert.NotEqual(firstBefore, await FingerprintAsync(first));
        Assert.Equal(secondBefore, await FingerprintAsync(second));
        Assert.Equal((1, 0), await CountsOfBindingAsync(first, runId));
        Assert.Equal("0", await ScalarAsync(first, "SELECT COUNT(*) FROM host_capability_snapshots WHERE \"Capability\" = 'GitHubCli'"));
        Assert.Equal("1", await ScalarAsync(second, "SELECT COUNT(*) FROM host_capability_snapshots WHERE \"Capability\" = 'GitHubCli'"));
        Assert.Equal("someone-else", await ScalarAsync(first, "SELECT \"Owner\" FROM github_repository_identity_bindings"));
        Assert.Equal("octo-cat", await ScalarAsync(second, "SELECT \"Owner\" FROM github_repository_identity_bindings"));
        Assert.Equal((1, 1), await CountsOfBindingAsync(second, runId));

        firstBound.Contexts.Answer = Matching(firstBound, "changed");
        secondBound.Contexts.Answer = Matching(secondBound, "untouched");
        var changed = await ObserveContextsAsync(firstBound.Host, runId);
        var untouched = await ObserveContextsAsync(secondBound.Host, runId);

        Assert.Equal("Unavailable", StatusOf(changed));
        Assert.Empty(firstBound.Contexts.Calls);
        Assert.Equal("ContextsReported", StatusOf(untouched));
        Assert.Single(secondBound.Contexts.Calls);
        Assert.Equal(secondBefore, await FingerprintAsync(second));
        AssertSealedSourceUnchanged();
    }

    [Fact]
    public async Task A_clone_provisioned_after_another_was_changed_and_disposed_starts_clean()
    {
        var (reference, _) = await LiveAsync();
        var clean = await DatabaseFingerprintAsync(reference.DatabasePath, [Snapshots]);
        var (changed, changedBound) = await LiveAsync();
        await ChangeAsync(
            changed,
            [Snapshots, "github_repository_identity_bindings", "events"],
            $"DELETE FROM {Snapshots}",
            "UPDATE github_repository_identity_bindings SET \"RepositoryNodeId\" = 'R_changed'",
            $"DELETE FROM events WHERE {EventOfBinding(changedBound.Ids.RunId)}");
        Assert.NotEqual(clean, await DatabaseFingerprintAsync(changed.DatabasePath, [Snapshots]));
        changed.Dispose();

        var (later, laterBound) = await LiveAsync();

        Assert.Equal(clean, await DatabaseFingerprintAsync(later.DatabasePath, [Snapshots]));
        Assert.Equal((1, 1), await CountsOfBindingAsync(later, laterBound.Ids.RunId));
        var binding = await GetBindingAsync(laterBound.Host, laterBound.Ids.RunId);
        Assert.Equal("Available", binding.State);
        Assert.Equal(NodeId, binding.Root.GetProperty("binding").GetProperty("repositoryNodeId").GetString());
        laterBound.Contexts.Answer = Matching(laterBound, "clean");
        Assert.Equal("ContextsReported", StatusOf(await ObserveContextsAsync(laterBound.Host, laterBound.Ids.RunId)));
        AssertSealedSourceUnchanged();
    }

    [Fact]
    public async Task Observer_answers_counters_and_clock_state_are_independent_between_live_clones()
    {
        var (_, first) = await LiveAsync();
        var (_, second) = await LiveAsync();

        Assert.NotSame(first.Contexts, second.Contexts);
        Assert.NotSame(first.IdentityObserver, second.IdentityObserver);
        Assert.NotSame(first.Clock, second.Clock);

        first.Contexts.Answer = Matching(first, "answered by the first");
        var answered = await ObserveContextsAsync(first.Host, first.Ids.RunId);
        var unanswered = await ObserveContextsAsync(second.Host, second.Ids.RunId);

        Assert.Equal("ContextsReported", StatusOf(answered));
        Assert.Equal("Unknown", StatusOf(unanswered));
        Assert.Single(first.Contexts.Calls);
        Assert.Single(second.Contexts.Calls);
        Assert.Empty(first.IdentityObserver.Calls);
        Assert.Empty(second.IdentityObserver.Calls);

        var recordedAt = await RecordedAtAsync(first);
        first.Clock.Fix(recordedAt.AddDays(-1));
        second.Contexts.Answer = Matching(second, "answered by the second");
        var earlier = await ObserveContextsAsync(first.Host, first.Ids.RunId);
        var unaffected = await ObserveContextsAsync(second.Host, second.Ids.RunId);
        first.Clock.Release();

        Assert.Equal("Unknown", StatusOf(earlier));
        Assert.Equal("ContextsReported", StatusOf(unaffected));
        Assert.Equal(2, first.Contexts.Calls.Count);
        Assert.Equal(2, second.Contexts.Calls.Count);
        Assert.True(
            unaffected.Root.GetProperty("observation").GetProperty("observedAtUtc").GetDateTimeOffset() > recordedAt,
            "the second clone's clock followed the first clone's fixed reading");
    }

    [Fact]
    public async Task Disposing_one_clone_preserves_the_other_and_the_sealed_source_until_their_owners_finish()
    {
        var (first, _) = await LiveAsync();
        var (second, secondBound) = await LiveAsync();
        var sealedPaths = new[]
        {
            evidence.SealedPath(GitHubCheckContextsSealedStage.TargetRecorded),
            evidence.SealedPath(GitHubCheckContextsSealedStage.BindingRecorded),
        };

        first.Dispose();

        Assert.False(File.Exists(first.DatabasePath));
        Assert.False(Directory.Exists(first.WritableRoot));
        Assert.True(File.Exists(second.DatabasePath));
        Assert.True(Directory.Exists(second.WritableRoot));
        secondBound.Contexts.Answer = Matching(secondBound, "survivor");
        Assert.Equal("ContextsReported", StatusOf(await ObserveContextsAsync(secondBound.Host, secondBound.Ids.RunId)));
        Assert.All(sealedPaths, path => Assert.True(File.Exists(path)));
        Assert.True(Directory.Exists(evidence.Scene.Root));
        AssertSealedSourceUnchanged();

        second.Dispose();

        Assert.False(File.Exists(second.DatabasePath));
        Assert.All(sealedPaths, path => Assert.True(File.Exists(path)));
        Assert.True(Directory.Exists(evidence.Scene.Root));
        AssertSealedSourceUnchanged();
    }

    [Fact]
    public async Task The_seed_executed_the_production_delivery_and_recorded_coherent_target_and_binding_evidence()
    {
        var identities = evidence.Identities;
        var runId = identities.Ids.RunId;
        var (bindingClone, bound) = await LiveAsync();
        var (targetClone, targetOnly) = await LiveAsync(GitHubCheckContextsSealedStage.TargetRecorded);

        var branchTip = evidence.Scene.RunMainGit("rev-parse", "refs/heads/" + evidence.Scene.BranchName).Trim();
        Assert.Equal(identities.Sha, branchTip);
        Assert.NotEqual(evidence.Scene.BaselineCommit, branchTip);
        Assert.Equal(evidence.Scene.BaselineCommit, evidence.Scene.RunMainGit("rev-parse", branchTip + "^").Trim());

        var receipt = await ReceiptAsync(bound.Host, runId);
        Assert.Equal(HttpStatusCode.OK, receipt.Status);
        Assert.Equal("Available", receipt.State);
        Assert.Contains(identities.Sha, receipt.Body, StringComparison.Ordinal);
        Assert.Contains(identities.OperationId.ToString(), receipt.Body, StringComparison.OrdinalIgnoreCase);

        var target = await GitHubObservationTargetTestBase.GetTargetAsync(bound.Host, runId);
        Assert.Equal("Available", target.State);
        Assert.Equal(identities.TargetId, target.Root.GetProperty("target").GetProperty("id").GetGuid());
        var binding = await GetBindingAsync(bound.Host, runId);
        Assert.Equal("Available", binding.State);
        Assert.Equal(NodeId, binding.Root.GetProperty("binding").GetProperty("repositoryNodeId").GetString());
        Assert.Equal((1, 1), await CountsOfBindingAsync(bindingClone, runId));
        Assert.Equal((1, 1), await CountsOfTargetAsync(bindingClone, runId));
        Assert.Equal("Completed", await ScalarAsync(bindingClone, "SELECT \"Status\" FROM local_commit_operations"));

        var targetOnlyBinding = await GetBindingAsync(targetOnly.Host, runId);
        Assert.Equal("NotRecorded", targetOnlyBinding.State);
        Assert.Equal((0, 0), await CountsOfBindingAsync(targetClone, runId));
        Assert.Equal((1, 1), await CountsOfTargetAsync(targetClone, runId));
        Assert.Equal(identities.TargetId, await GetTargetByRowAsync(targetClone));
        AssertSealedSourceUnchanged();
    }

    [Fact]
    public async Task An_observation_host_runs_no_supervisor_and_refuses_providers_and_git_writers_against_the_sealed_scene()
    {
        var sceneBefore = evidence.SceneFingerprint();
        var (clone, bound) = await LiveAsync();

        var hostedServices = bound.Host.Services.GetServices<IHostedService>()
            .Select(service => service.GetType().Namespace ?? string.Empty)
            .Where(space => space.StartsWith("DevalCopilot.Api.HostedServices", StringComparison.Ordinal));
        Assert.Empty(hostedServices);

        var refused = 0;
        foreach (var port in SealedSceneGuard.GuardedPorts)
        {
            var service = bound.Host.Services.GetRequiredService(port);
            Assert.IsAssignableFrom<SealedSceneGuard.RefusingProxy>(service);
            var mutating = port.GetMethods().Where(method => !IsReader(port, method)).ToArray();
            Assert.NotEmpty(mutating);
            foreach (var method in mutating)
            {
                var arguments = method.GetParameters()
                    .Select(parameter => parameter.ParameterType.IsValueType ? Activator.CreateInstance(parameter.ParameterType) : null)
                    .ToArray();
                var thrown = Assert.Throws<TargetInvocationException>(() => method.Invoke(service, arguments));
                Assert.IsType<InvalidOperationException>(thrown.InnerException);
                refused++;
            }
        }

        Assert.Equal(refused, clone.Refusals.Count);
        bound.Contexts.Answer = Matching(bound, "observed");
        Assert.Equal("ContextsReported", StatusOf(await ObserveContextsAsync(bound.Host, bound.Ids.RunId)));
        Assert.Equal(refused, clone.Refusals.Count);
        Assert.Equal(sceneBefore, evidence.SceneFingerprint());
        AssertSealedSourceUnchanged();
    }

    [Fact]
    public void Mutating_the_capture_input_cannot_alter_the_sealed_identities()
    {
        var (a, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var source = new List<Guid> { a, b, c };
        var emptySource = new List<Guid>();
        var withMembers = Lineage(source);
        var scalars = ScalarsOf(withMembers);
        var members = GitHubCheckContextsSealedIdentities.Capture(withMembers, Guid.NewGuid(), "sha", Guid.NewGuid());
        var none = GitHubCheckContextsSealedIdentities.Capture(Lineage(null), Guid.NewGuid(), "sha", Guid.NewGuid());
        var empty = GitHubCheckContextsSealedIdentities.Capture(Lineage(emptySource), Guid.NewGuid(), "sha", Guid.NewGuid());

        source.Reverse();
        source[0] = Guid.NewGuid();
        source.Add(Guid.NewGuid());
        emptySource.Add(Guid.NewGuid());

        Assert.Equal([a, b, c], members.Ids.AllExecutionIds!.ToArray());
        source.Clear();
        Assert.Equal([a, b, c], members.Ids.AllExecutionIds!.ToArray());
        Assert.Equal(scalars, ScalarsOf(members.Ids));
        Assert.Null(none.Ids.AllExecutionIds);
        Assert.NotNull(empty.Ids.AllExecutionIds);
        Assert.Empty(empty.Ids.AllExecutionIds);
        AssertNoMutableMembership(members.Ids.AllExecutionIds);
        AssertNoMutableMembership(empty.Ids.AllExecutionIds);
    }

    [Fact]
    public async Task Live_clones_expose_no_shared_mutable_membership_and_attempted_mutation_reaches_no_other_clone_the_sealed_identities_or_a_later_clone()
    {
        var expectedMembers = evidence.Identities.Ids.AllExecutionIds!.ToArray();
        var expectedScalars = ScalarsOf(evidence.Identities.Ids);
        Assert.NotEmpty(expectedMembers);
        var (_, first) = await LiveAsync();
        var (_, second) = await LiveAsync();

        TryMutate(first.Ids.AllExecutionIds);
        TryMutate(second.Ids.AllExecutionIds);
        var (_, later) = await LiveAsync();

        foreach (var ids in new[] { first.Ids, second.Ids, later.Ids, evidence.Identities.Ids, evidence.Identities.IdsForClone() })
        {
            Assert.Equal(expectedMembers, ids.AllExecutionIds!.ToArray());
            Assert.Equal(expectedScalars, ScalarsOf(ids));
        }

        Assert.Equal(first.Ids.ExecutionId, first.Ids.AllExecutionIds![0]);
        foreach (var ids in new[] { first.Ids, second.Ids, later.Ids })
        {
            AssertNoMutableMembership(ids.AllExecutionIds);
        }
    }

    [Fact]
    public void The_original_ordered_membership_and_scalar_identities_remain_intact_after_every_attempted_mutation_of_a_copy()
    {
        Guid[] members = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
        var original = Lineage(new List<Guid>(members));
        var scalars = ScalarsOf(original);
        var captured = GitHubCheckContextsSealedIdentities.Capture(original, Guid.NewGuid(), "sha", Guid.NewGuid());
        var one = captured.IdsForClone();
        var two = captured.IdsForClone();

        TryMutate(one.AllExecutionIds);
        TryMutate(two.AllExecutionIds);
        var three = captured.IdsForClone();

        foreach (var ids in new[] { captured.Ids, one, two, three })
        {
            Assert.Equal(members, ids.AllExecutionIds!.ToArray());
            Assert.Equal(scalars, ScalarsOf(ids));
        }

        Assert.Equal(members, original.AllExecutionIds!.ToArray());
    }

    private static LocalCommitLineageIds Lineage(IReadOnlyList<Guid>? members) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        members);

    private static Guid[] ScalarsOf(LocalCommitLineageIds ids) =>
    [
        ids.ProjectId, ids.RunId, ids.WorkspaceId, ids.LeaseId, ids.CheckpointId, ids.ReviewAttemptId, ids.ExecutionReportId,
        ids.ExecutionId, ids.CommandId, ids.HumanReviewId,
    ];

    /// <summary>Every way a borrower could change the membership it was handed. A mutable list is changed (which the callers then
    /// observe in every other holder); a read-only one refuses each attempt.</summary>
    private static void TryMutate(IReadOnlyList<Guid>? members)
    {
        if (members is List<Guid> list)
        {
            list.Reverse();
            list.Add(Guid.NewGuid());
            return;
        }

        if (members is ICollection<Guid> collection)
        {
            try
            {
                collection.Add(Guid.NewGuid());
            }
            catch (NotSupportedException)
            {
            }
        }
    }

    private static void AssertNoMutableMembership(IReadOnlyList<Guid>? members)
    {
        Assert.NotNull(members);
        Assert.False(members is List<Guid>, "the membership is a mutable list");
        Assert.False(members is Guid[], "the membership is a mutable array");
        var list = Assert.IsAssignableFrom<IList<Guid>>(members);
        Assert.True(list.IsReadOnly, "the membership is not read-only");
        Assert.Throws<NotSupportedException>(() => list.Add(Guid.NewGuid()));
        Assert.Throws<NotSupportedException>(() => list.Clear());
        if (list.Count > 0)
        {
            Assert.Throws<NotSupportedException>(() => list.Remove(list[0]));
            Assert.Throws<NotSupportedException>(() => list[0] = Guid.NewGuid());
            Assert.Throws<NotSupportedException>(() => list.Insert(0, Guid.NewGuid()));
        }
    }

    private static bool IsReader(Type port, MethodInfo method) =>
        (port == typeof(ILocalCommitRepository) && method.Name == nameof(ILocalCommitRepository.InspectAsync))
        || (port == typeof(IGitWorktreeAdapter) && method.Name != nameof(IGitWorktreeAdapter.CreateAsync));

    private static async Task<DateTimeOffset> RecordedAtAsync(Bound bound)
    {
        var reading = await GetBindingAsync(bound.Host, bound.Ids.RunId);
        return reading.Root.GetProperty("binding").GetProperty("recordedAtUtc").GetDateTimeOffset();
    }

    private static async Task<(int Bindings, int Events)> CountsOfBindingAsync(GitHubCheckContextsSealedClone clone, Guid runId) => (
        int.Parse(await ScalarAsync(clone, "SELECT COUNT(*) FROM github_repository_identity_bindings"), System.Globalization.CultureInfo.InvariantCulture),
        int.Parse(await ScalarAsync(clone, $"SELECT COUNT(*) FROM events WHERE {EventOfBinding(runId)}"), System.Globalization.CultureInfo.InvariantCulture));

    private static async Task<(int Targets, int Events)> CountsOfTargetAsync(GitHubCheckContextsSealedClone clone, Guid runId) => (
        int.Parse(await ScalarAsync(clone, "SELECT COUNT(*) FROM github_observation_targets"), System.Globalization.CultureInfo.InvariantCulture),
        int.Parse(await ScalarAsync(clone, $"SELECT COUNT(*) FROM events WHERE {EventOfTarget(runId)}"), System.Globalization.CultureInfo.InvariantCulture));

    private static async Task<Guid> GetTargetByRowAsync(GitHubCheckContextsSealedClone clone) =>
        Guid.Parse(await ScalarAsync(clone, "SELECT \"Id\" FROM github_observation_targets"));

    private static async Task<string> ScalarAsync(GitHubCheckContextsSealedClone clone, string statement)
    {
        await using var connection = new SqliteConnection($"Data Source={clone.DatabasePath};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // Test-only SQL assembled from constants and identifiers this test generated.
        command.CommandText = statement;
#pragma warning restore CA2100
        return Convert.ToString(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture)!;
    }
}
