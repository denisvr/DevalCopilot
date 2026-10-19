using DevalCopilot.Application.Features.Projects.Ports;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.AgentSupervisions;

/// <summary>The Git evidence double of every observed supervisor composition: a deterministic result per numbered call, and one call that
/// can be held until released. It honors the caller's token only while held, exactly like a real capture that is cancelled.</summary>
internal sealed class GateableEvidenceReader(Func<int, GitWorkspaceEvidenceResult> resultForCall) : IGitWorkspaceEvidenceReader
{
    private readonly TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int calls;
    private int blockedCall = -1;

    public int CallCount => Volatile.Read(ref calls);

    /// <summary>Completes when the held call has been reached.</summary>
    public Task Entered => entered.Task;

    /// <summary>Holds the call that is <paramref name="ordinalFromNow"/> calls from now (1 is the very next call).</summary>
    public void HoldCall(int ordinalFromNow) => Volatile.Write(ref blockedCall, CallCount + ordinalFromNow);

    public void Release() => gate.TrySetResult();

    public async Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken)
    {
        var call = Interlocked.Increment(ref calls);
        if (call == Volatile.Read(ref blockedCall))
        {
            entered.TrySetResult();
            await gate.Task.WaitAsync(cancellationToken);
        }

        return resultForCall(call);
    }

    public static GitWorkspaceEvidenceResult Matching(string fingerprintSha256) =>
        new(GitWorkspaceEvidenceOutcome.Success, new string('a', 40), fingerprintSha256, [], null);
}
