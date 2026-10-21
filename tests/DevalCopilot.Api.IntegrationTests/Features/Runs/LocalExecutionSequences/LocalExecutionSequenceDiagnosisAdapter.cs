using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionSequences;

/// <summary>The deterministic Codex failure-diagnosis boundary: it reads the exact sealed manifest it is handed, counts its invocations
/// and answers each one with the next scripted answer (one finding when none is scripted).</summary>
internal sealed class LocalExecutionSequenceDiagnosisAdapter(IArtifactStore artifactStore) : ICodexVerificationDiagnosisAdapter
{
    private int invocations;

    public int InvocationCount => Volatile.Read(ref invocations);

    public Queue<string> Answers { get; } = new();

    public List<(Guid AttemptId, string Text)> ReceivedManifests { get; } = [];

    public async Task<VerificationDiagnosisInvocationResult> InvokeAsync(
        VerificationDiagnosisInvocationRequest request, CancellationToken cancellationToken)
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

        string answer;
        lock (Answers)
        {
            answer = Answers.Count > 0 ? Answers.Dequeue() : LocalExecutionSequenceResponses.DiagnosisFindings(1);
        }

        await LocalExecutionSequenceOutputs.WriteAsync(artifactStore, request.RunId, request.AttemptId, answer, cancellationToken);
        return new VerificationDiagnosisInvocationResult(
            VerificationDiagnosisInvocationOutcome.Exited, false, false, null, ProcessEvidence: TestProcessEvidence.ReportedCleanExit);
    }
}
