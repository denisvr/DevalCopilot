using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Infrastructure.Features.Projects;
using DevalCopilot.Infrastructure.Features.Runs;
using DevalCopilot.Infrastructure.IntegrationTests.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Runs;

/// <summary>
/// An invalid host-owned control state (a non-empty Git configuration or any hooks entry) must stop every local-commit entry point
/// before a single process is started, with the sentinel bytes unexecuted and unchanged. The adapter under test is composed over a
/// process adapter that records every request and refuses to run it, so "no Git command" is a measured fact, not an inference.
/// </summary>
[Collection(EnvironmentPathMutationCollection.Name)]
public sealed class LocalCommitInvalidControlStateTests : IDisposable
{
    private const string ConfigSentinel = "[core]\n\thooksPath = sentinel\n";

    private readonly LocalCommitScene _scene = new();
    private readonly RecordingProcessAdapter _processes = new();

    public void Dispose() => _scene.Dispose();

    private sealed class RecordingProcessAdapter : IProcessExecutionAdapter
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            throw new InvalidOperationException("a process was started while the control storage was invalid");
        }
    }

    private LocalCommitGit RecordedGit() =>
        new(_processes, new GitWorktreeAdapter(_processes), _scene.Markers, _scene.Storage);

    public static TheoryData<string> InvalidStates => new() { "configuration", "hook" };

    private string Corrupt(string kind)
    {
        var path = kind == "configuration"
            ? _scene.Storage.EmptyConfigPath
            : Path.Combine(_scene.Storage.HooksDirectory, "reference-transaction");
        var marker = Path.Combine(_scene.Root, "hook-ran.txt");
        File.WriteAllText(path, kind == "configuration" ? ConfigSentinel : $"#!/bin/sh\necho ran > \"{marker}\"\n");
        return path;
    }

    private void AssertSentinelUnexecutedAndUnchanged(string path, byte[] before)
    {
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.False(File.Exists(Path.Combine(_scene.Root, "hook-ran.txt")));
        Assert.Equal(0, _processes.Calls);
    }

    [Theory]
    [MemberData(nameof(InvalidStates))]
    public async Task Preparation_refuses_before_any_process_when_the_control_state_is_invalid(string kind)
    {
        _scene.WriteWorkspace("a.txt", "x\n");
        var (fingerprint, changes) = await _scene.CaptureAsync();
        Assert.True(_scene.Storage.TryEnsureHooksDirectoryEmpty());
        var sentinel = Corrupt(kind);
        var before = File.ReadAllBytes(sentinel);
        var request = new LocalCommitPreparationRequest(
            Guid.NewGuid(), _scene.MainPath, _scene.WorkspacePath, _scene.BranchName, _scene.BaselineCommit, fingerprint, changes,
            LocalCommitScene.Message, _scene.Ownership, DateTimeOffset.FromUnixTimeSeconds(1_800_000_000));

        var result = await RecordedGit().PrepareAsync(request, CancellationToken.None);

        Assert.Equal(LocalCommitPreparationOutcome.HooksDirectoryNotEmpty, result.Outcome);
        AssertSentinelUnexecutedAndUnchanged(sentinel, before);
        Assert.Empty(_scene.StorageLeaves());
    }

    [Theory]
    [MemberData(nameof(InvalidStates))]
    public async Task Execution_inspection_and_recovery_acquisition_refuse_when_a_valid_storage_later_becomes_invalid(string kind)
    {
        _scene.WriteWorkspace("a.txt", "approved change\n");
        var (prepared, request) = await _scene.PrepareAsync();
        Assert.Equal(LocalCommitPreparationOutcome.Prepared, prepared.Outcome);
        var facts = _scene.FactsFor(request, prepared.Facts!);
        var indexBefore = File.ReadAllBytes(_scene.IndexPath);
        var sentinel = Corrupt(kind);
        var before = File.ReadAllBytes(sentinel);
        var git = RecordedGit();

        var execution = await git.ExecuteAsync(facts, CancellationToken.None);
        var inspection = await git.InspectAsync(facts, CancellationToken.None);
        var pending = await git.AcquirePendingIndexEffectsAsync(facts, "devalcopilot-quarantine.index", CancellationToken.None);

        Assert.Equal(LocalCommitExecutionOutcome.NotPromoted, execution.Outcome);
        Assert.Equal("local_commit.index_acquisition_unproven", execution.ReasonCode);
        Assert.Equal(LocalCommitInspectionOutcome.Unprovable, inspection.Outcome);
        Assert.Null(pending);
        AssertSentinelUnexecutedAndUnchanged(sentinel, before);
        Assert.Equal(_scene.BaselineCommit, _scene.RunWorkspaceGit("rev-parse", "refs/heads/" + _scene.BranchName).Trim());
        Assert.Equal(indexBefore, File.ReadAllBytes(_scene.IndexPath));
        Assert.False(File.Exists(_scene.IndexPath + ".lock"));
        Assert.True(File.Exists(_scene.Storage.ResolveArtifact(facts.PreparedIndexRelativePath)));
    }
}
