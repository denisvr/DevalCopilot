using DevalCopilot.Application.Features.Runs.Ports;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// The scripted preparer's completion capture, proven without a host or Git by an inner preparer whose results the test finishes: a
/// completion exists only for a result that was actually returned, a refusal, a failure or a self-contradicting result is never a
/// Prepared artifact, method entry and outstanding calls satisfy no boundary, and a signal is tied to the captured result rather
/// than to the callback. These cases are what make "two real Prepared results" a fact the competing-preparation tests establish
/// instead of assume.
/// </summary>
public sealed class ScriptedLocalCommitPreparerTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    private static readonly string Fingerprint = new('f', 64);

    private static LocalCommitPreparationRequest Request() => new(
        Guid.NewGuid(), "main", "workspace", "branch", new string('a', 40), Fingerprint, [], "message",
        new LocalCommitOwnership(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1UL, "00"), DateTimeOffset.UnixEpoch);

    private static LocalCommitPreparedFacts Facts(string artifact) => new(
        new string('b', 40), new string('c', 40), "name", "email", 1, new string('d', 64), new string('e', 64), artifact, 1, 1);

    private static LocalCommitPreparationResult Prepared(string artifact) =>
        new(LocalCommitPreparationOutcome.Prepared, Facts(artifact));

    private static LocalCommitPreparationResult Refused => new(LocalCommitPreparationOutcome.CheckpointNotCurrent, null);

    private static (ScriptedLocalCommitPreparer Script, BlockingPreparer Inner, List<LocalCommitPreparationCompletion> Seen) Create()
    {
        var inner = new BlockingPreparer();
        var seen = new List<LocalCommitPreparationCompletion>();
        var script = new ScriptedLocalCommitPreparer().Attach(inner);
        script.AfterPrepare = completion =>
        {
            lock (seen)
            {
                seen.Add(completion);
            }

            return Task.CompletedTask;
        };
        return (script, inner, seen);
    }

    [Fact]
    public async Task Method_entry_and_outstanding_preparations_satisfy_no_boundary()
    {
        var (script, inner, seen) = Create();

        var first = script.PrepareAsync(Request(), CancellationToken.None);
        var second = script.PrepareAsync(Request(), CancellationToken.None);

        Assert.Equal(2, script.Entered);
        Assert.Equal(2, inner.Calls);
        Assert.Empty(script.Completions);
        Assert.False(script.WhenCompletedAsync(1).IsCompleted);
        Assert.False(script.WhenPreparedAsync(1).IsCompleted);
        Assert.Empty(seen);
        Assert.False(first.IsCompleted || second.IsCompleted);

        inner.Finish(0, Prepared("operations\\one\\prepared.index"));
        inner.Finish(1, Prepared("operations\\two\\prepared.index"));
        await first.WaitAsync(Bound);
        await second.WaitAsync(Bound);
        Assert.Equal(2, script.Completions.Count);
    }

    [Fact]
    public async Task A_refusal_is_captured_with_its_outcome_and_is_never_a_prepared_artifact()
    {
        var (script, inner, seen) = Create();
        var call = script.PrepareAsync(Request(), CancellationToken.None);
        var prepared = script.WhenPreparedAsync(1);

        inner.Finish(0, Refused);
        var returned = await call.WaitAsync(Bound);

        Assert.Equal(LocalCommitPreparationOutcome.CheckpointNotCurrent, returned.Outcome);
        var completion = Assert.Single(await script.WhenCompletedAsync(1).WaitAsync(Bound));
        Assert.Same(completion, Assert.Single(seen));
        Assert.Equal(1, completion.Sequence);
        Assert.False(completion.IsPrepared);
        Assert.Null(completion.ArtifactRelativePath);
        Assert.False(prepared.IsCompleted, "a refusal must not satisfy a Prepared boundary");
        Assert.False(script.WhenPreparedAsync(1).IsCompleted);
    }

    [Theory]
    [InlineData(LocalCommitPreparationOutcome.Prepared, false)]
    [InlineData(LocalCommitPreparationOutcome.GitFailed, true)]
    [InlineData(LocalCommitPreparationOutcome.CheckpointNotCurrent, true)]
    public async Task A_result_that_contradicts_itself_supplies_no_artifact_authority(
        LocalCommitPreparationOutcome outcome, bool carriesFacts)
    {
        var (script, inner, _) = Create();
        var call = script.PrepareAsync(Request(), CancellationToken.None);

        inner.Finish(0, new LocalCommitPreparationResult(outcome, carriesFacts ? Facts("operations\\x\\prepared.index") : null));
        await call.WaitAsync(Bound);

        var completion = Assert.Single(script.Completions);
        Assert.False(completion.IsPrepared);
        Assert.Null(completion.ArtifactRelativePath);
        Assert.False(script.WhenPreparedAsync(1).IsCompleted);
    }

    [Fact]
    public async Task A_prepared_result_supplies_exactly_its_own_artifact_in_capture_order_and_the_boundary_needs_both()
    {
        var (script, inner, _) = Create();
        var calls = new[]
        {
            script.PrepareAsync(Request(), CancellationToken.None),
            script.PrepareAsync(Request(), CancellationToken.None),
            script.PrepareAsync(Request(), CancellationToken.None),
        };
        var twoPrepared = script.WhenPreparedAsync(2);

        // Capture order is the order the real results returned, not the order the calls entered.
        inner.Finish(2, Prepared("operations\\late\\prepared.index"));
        await calls[2].WaitAsync(Bound);
        inner.Finish(0, Refused);
        await calls[0].WaitAsync(Bound);
        Assert.False(twoPrepared.IsCompleted, "one Prepared result and one refusal are not two Prepared results");

        inner.Finish(1, Prepared("operations\\early\\prepared.index"));
        await calls[1].WaitAsync(Bound);
        var prepared = await twoPrepared.WaitAsync(Bound);

        Assert.Equal(new[] { 1, 3 }, prepared.Select(completion => completion.Sequence));
        Assert.Equal(
            new[] { "operations\\late\\prepared.index", "operations\\early\\prepared.index" },
            prepared.Select(completion => completion.ArtifactRelativePath));
        Assert.All(prepared, completion =>
            Assert.Equal(completion.Result.Facts!.PreparedIndexRelativePath, completion.ArtifactRelativePath));
        Assert.Equal(new[] { 1, 2, 3 }, script.Completions.Select(completion => completion.Sequence));
        Assert.Equal(LocalCommitPreparationOutcome.CheckpointNotCurrent, script.Completions[1].Result.Outcome);
    }

    [Fact]
    public async Task A_preparation_that_throws_is_counted_as_faulted_and_is_never_a_completion()
    {
        var (script, inner, seen) = Create();
        var call = script.PrepareAsync(Request(), CancellationToken.None);

        inner.Fail(0, new InvalidOperationException("git failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => call);
        Assert.Equal(1, script.Faulted);
        Assert.Equal(1, script.Entered);
        Assert.Empty(script.Completions);
        Assert.Empty(seen);
        Assert.False(script.WhenCompletedAsync(1).IsCompleted);
        Assert.False(script.WhenPreparedAsync(1).IsCompleted);
    }

    [Fact]
    public async Task The_result_is_captured_and_signalled_before_the_callback_and_a_parked_callback_does_not_hide_it()
    {
        var inner = new BlockingPreparer();
        var parked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource<LocalCommitPreparationCompletion>(TaskCreationOptions.RunContinuationsAsynchronously);
        var script = new ScriptedLocalCommitPreparer().Attach(inner);
        script.AfterPrepare = completion =>
        {
            entered.TrySetResult(completion);
            return parked.Task;
        };
        var call = script.PrepareAsync(Request(), CancellationToken.None);

        inner.Finish(0, Prepared("operations\\own\\prepared.index"));
        var seen = await entered.Task.WaitAsync(Bound);

        // The callback is still parked: the call has not returned, but the completion was already captured and signalled, so the
        // test (not the callback) decides when this request proceeds.
        Assert.False(call.IsCompleted);
        var signalled = Assert.Single(await script.WhenPreparedAsync(1).WaitAsync(Bound));
        Assert.Same(seen, signalled);
        Assert.Equal("operations\\own\\prepared.index", signalled.ArtifactRelativePath);

        parked.SetResult();
        var returned = await call.WaitAsync(Bound);
        Assert.Same(signalled.Result, returned);
    }

    [Fact]
    public async Task An_induced_refusal_changes_only_the_selected_calls_request_and_the_inner_preparer_decides_the_result()
    {
        var (script, inner, _) = Create();
        script.InduceCheckpointNotCurrentFor = ordinal => ordinal == 2;
        var requests = new[] { Request(), Request(), Request() };

        var calls = requests.Select(request => script.PrepareAsync(request, CancellationToken.None)).ToArray();

        Assert.Equal(requests[0], inner.Received(0));
        Assert.Equal(requests[2], inner.Received(2));
        Assert.Equal(new string('0', 64), inner.Received(1).CheckpointFingerprintSha256);
        Assert.Equal(requests[1] with { CheckpointFingerprintSha256 = new string('0', 64) }, inner.Received(1));
        for (var index = 0; index < calls.Length; index++)
        {
            inner.Finish(index, index == 1 ? Refused : Prepared($"operations\\{index}\\prepared.index"));
        }

        await Task.WhenAll(calls).WaitAsync(Bound);
        Assert.Equal(inner.Received(1), script.Completions.Single(completion => !completion.IsPrepared).Request);
        Assert.Equal(2, script.Completions.Count(completion => completion.IsPrepared));
    }

    /// <summary>An inner preparer whose calls block until the test finishes or fails them.</summary>
    private sealed class BlockingPreparer : ILocalCommitPreparer
    {
        private readonly List<(LocalCommitPreparationRequest Request, TaskCompletionSource<LocalCommitPreparationResult> Source)> _calls = [];

        public int Calls
        {
            get
            {
                lock (_calls)
                {
                    return _calls.Count;
                }
            }
        }

        public LocalCommitPreparationRequest Received(int index)
        {
            lock (_calls)
            {
                return _calls[index].Request;
            }
        }

        public void Finish(int index, LocalCommitPreparationResult result) => Source(index).SetResult(result);

        public void Fail(int index, Exception exception) => Source(index).SetException(exception);

        public Task<LocalCommitPreparationResult> PrepareAsync(LocalCommitPreparationRequest request, CancellationToken cancellationToken)
        {
            var source = new TaskCompletionSource<LocalCommitPreparationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_calls)
            {
                _calls.Add((request, source));
            }

            return source.Task;
        }

        private TaskCompletionSource<LocalCommitPreparationResult> Source(int index)
        {
            lock (_calls)
            {
                return _calls[index].Source;
            }
        }
    }
}
