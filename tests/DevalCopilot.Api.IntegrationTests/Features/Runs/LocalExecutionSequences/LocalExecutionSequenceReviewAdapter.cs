using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionSequences;

/// <summary>The deterministic Codex ordinary-review boundary: it reads the exact sealed manifest it is handed, counts its invocations and
/// answers each one with the next scripted answer (an approval when none is scripted).</summary>
internal sealed class LocalExecutionSequenceReviewAdapter(IArtifactStore artifactStore) : ICodexImplementationReviewAdapter
{
    private int invocations;

    public int InvocationCount => Volatile.Read(ref invocations);

    public Queue<string> Answers { get; } = new();

    public List<(Guid AttemptId, string Text)> ReceivedManifests { get; } = [];

    /// <summary>When set, an invocation waits for it and ignores cancellation: a provider process the host can no longer reach.</summary>
    public TaskCompletionSource? Hold { get; set; }

    public async Task<ImplementationReviewInvocationResult> InvokeAsync(
        ImplementationReviewInvocationRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref invocations);
        var window = await artifactStore.VerifyAndReadSealedAsync(
            request.ContextManifestRelativeStoragePath, request.ContextManifestByteLength, request.ContextManifestContentHash,
            0, 64 * 1024, cancellationToken);
        Assert.Equal(SealedReadStatus.Ok, window.Status);
        lock (ReceivedManifests)
        {
            ReceivedManifests.Add((request.AttemptId, window.Text));
        }

        if (Hold is { } hold)
        {
            await hold.Task;
        }

        string answer;
        lock (Answers)
        {
            answer = Answers.Count > 0 ? Answers.Dequeue() : LocalExecutionSequenceResponses.ApprovedJson;
        }

        await LocalExecutionSequenceOutputs.WriteAsync(artifactStore, request.RunId, request.AttemptId, answer, cancellationToken);
        return new ImplementationReviewInvocationResult(
            ImplementationReviewInvocationOutcome.Exited, false, false, null, ProcessEvidence: TestProcessEvidence.ReportedCleanExit);
    }
}
