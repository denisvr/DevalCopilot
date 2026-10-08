using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.IntegrationTests.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Runs;

/// <summary>
/// ADR-0029 R2 at the observation seams, against real Git: repository-configured executables never run across preparation,
/// execution, inspection and the post-commit observation; the fixed private profile keeps the accepted checkpoint fingerprint
/// byte-for-byte; and a presentation the fixed profile cannot reproduce refuses rather than being waived.
/// </summary>
[Collection(EnvironmentPathMutationCollection.Name)]
public sealed class LocalCommitGitObservationTests : IDisposable
{
    private readonly LocalCommitScene _scene = new();

    public void Dispose() => _scene.Dispose();

    private string Sentinel(string name) => Path.Combine(_scene.Root, name + ".ran");

    private static string Command(string sentinel) => $"printf ran > '{sentinel.Replace('\\', '/')}'; cat";

    [Fact]
    public async Task Repository_configured_executables_never_run_through_preparation_execution_inspection_and_observation()
    {
        _scene.WriteWorkspace("a.txt", "approved change\n");
        File.Delete(Path.Combine(_scene.WorkspacePath, "d.txt"));
        // The ordinary checkpoint is captured first, exactly as a real checkpoint would be, before anything is planted.
        var (fingerprint, changes) = await _scene.CaptureAsync();
        var planted = new[] { "fsmonitor", "external-diff", "pager", "textconv", "clean" };
        _scene.RunMainGit("config", "core.fsmonitor", Command(Sentinel("fsmonitor")));
        _scene.RunMainGit("config", "diff.external", Command(Sentinel("external-diff")));
        _scene.RunMainGit("config", "core.pager", Command(Sentinel("pager")));
        _scene.RunMainGit("config", "diff.sentinel.textconv", Command(Sentinel("textconv")));
        _scene.RunMainGit("config", "filter.sentinel.clean", Command(Sentinel("clean")));

        var (result, request) = await _scene.PrepareAsync(fingerprintOverride: fingerprint, changesOverride: changes);

        Assert.Equal(LocalCommitPreparationOutcome.Prepared, result.Outcome);
        var facts = _scene.FactsFor(request, result.Facts!);
        var executed = await _scene.Git.ExecuteAsync(facts, CancellationToken.None);
        var inspection = await _scene.Git.InspectAsync(facts, CancellationToken.None);

        Assert.True(executed.Outcome == LocalCommitExecutionOutcome.Promoted, executed.ReasonCode);
        Assert.True(executed.SourceConsistent, "the controlled post-commit observation is a positive proof, not a constant");
        Assert.True(inspection.SourceConsistent);
        foreach (var name in planted)
        {
            Assert.False(File.Exists(Sentinel(name)), $"the {name} executable ran");
        }
    }

    [Fact]
    public async Task Configuration_and_info_attributes_injected_during_preparation_never_run_and_never_alter_the_fingerprint()
    {
        _scene.WriteWorkspace("a.txt", "approved change\n");
        var (fingerprint, changes) = await _scene.CaptureAsync();
        var injected = false;
        var git = _scene.CreateGit(new MutatingAdapter(new ChildProcessExecutionAdapter(), request =>
        {
            if (injected || !request.Arguments.Contains("status"))
            {
                return;
            }

            injected = true;
            _scene.RunMainGit("config", "filter.injected.clean", Command(Sentinel("injected")));
            File.WriteAllText(Path.Combine(_scene.CommonDirectory, "info", "attributes"), "* filter=injected\n");
        }));
        var request = new LocalCommitPreparationRequest(
            Guid.NewGuid(), _scene.MainPath, _scene.WorkspacePath, _scene.BranchName, _scene.BaselineCommit, fingerprint, changes,
            LocalCommitScene.Message, _scene.Ownership, DateTimeOffset.FromUnixTimeSeconds(1_800_000_000));

        var result = await git.PrepareAsync(request, CancellationToken.None);

        // Whatever the outcome (the earlier immutable-source proof may already refuse), the planted executable never ran.
        Assert.False(File.Exists(Sentinel("injected")));
        Assert.True(result.Outcome is LocalCommitPreparationOutcome.Prepared or LocalCommitPreparationOutcome.ConversionRefused
            or LocalCommitPreparationOutcome.SourceChanged or LocalCommitPreparationOutcome.CheckpointNotCurrent, result.Outcome.ToString());
    }

    [Fact]
    public async Task A_presentation_the_fixed_profile_cannot_reproduce_refuses_instead_of_being_waived()
    {
        // A repository that quotes non-ASCII paths differently changes the ordinary checkpoint's raw diff bytes. The fixed private
        // profile must not silently accept or "try profiles until the hash matches": the whole commit is refused.
        _scene.RunMainGit("config", "core.quotePath", "false");
        _scene.WriteWorkspace("naïve-漢.txt", "changed unicode\n");
        var (fingerprint, changes) = await _scene.CaptureAsync();

        var (result, request) = await _scene.PrepareAsync(fingerprintOverride: fingerprint, changesOverride: changes);

        Assert.NotEqual(LocalCommitPreparationOutcome.Prepared, result.Outcome);
        Assert.Equal(_scene.BaselineCommit, _scene.RunWorkspaceGit("rev-parse", "refs/heads/" + _scene.BranchName).Trim());
        Assert.False(Directory.Exists(_scene.Storage.OperationDirectory(request.OperationId)));
    }

    [Fact]
    public async Task A_changed_tracked_executable_unicode_added_deleted_and_binary_set_agrees_with_the_ordinary_fingerprint_and_a_content_edit_refuses()
    {
        _scene.WriteWorkspace("naïve-漢.txt", "changed unicode\n");
        _scene.WriteWorkspace("added.txt", "new\n");
        _scene.WriteWorkspaceBytes("binary.bin", [0, 1, 0, 255, 42]);
        _scene.WriteWorkspace("run.sh", "#!/bin/sh\necho changed\n");
        File.Delete(Path.Combine(_scene.WorkspacePath, "d.txt"));
        var (fingerprint, changes) = await _scene.CaptureAsync();

        var (positive, request) = await _scene.PrepareAsync(fingerprintOverride: fingerprint, changesOverride: changes);
        Assert.Equal(LocalCommitPreparationOutcome.Prepared, positive.Outcome);
        Assert.Contains("100755 blob", _scene.RunMainGit("ls-tree", "-r", positive.Facts!.CommitSha).Split('\n').Single(
            line => line.Contains("\trun.sh", StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.NotEqual(Guid.Empty, request.OperationId);

        // The same approved fingerprint no longer matches once any observed byte changes, whichever path it is on.
        _scene.WriteWorkspace("added.txt", "new but edited\n");
        var (stale, _) = await _scene.PrepareAsync(fingerprintOverride: fingerprint, changesOverride: changes);
        Assert.NotEqual(LocalCommitPreparationOutcome.Prepared, stale.Outcome);
    }

    [Fact]
    public async Task An_untracked_file_added_after_the_commit_makes_the_post_commit_observation_inconsistent_not_clean()
    {
        _scene.WriteWorkspace("a.txt", "approved change\n");
        var (result, request) = await _scene.PrepareAsync();
        Assert.Equal(LocalCommitPreparationOutcome.Prepared, result.Outcome);
        var facts = _scene.FactsFor(request, result.Facts!);
        var executed = await _scene.Git.ExecuteAsync(facts, CancellationToken.None);
        Assert.True(executed.Outcome == LocalCommitExecutionOutcome.Promoted, executed.ReasonCode);
        Assert.True((await _scene.Git.InspectAsync(facts, CancellationToken.None)).SourceConsistent);

        _scene.WriteWorkspace("stray.txt", "untracked after delivery\n");

        Assert.False((await _scene.Git.InspectAsync(facts, CancellationToken.None)).SourceConsistent);
    }

    private sealed class MutatingAdapter(IProcessExecutionAdapter inner, Action<ProcessExecutionRequest> before) : IProcessExecutionAdapter
    {
        public Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
        {
            before(request);
            return inner.ExecuteAsync(request, cancellationToken);
        }
    }
}
