using DevalCopilot.Application.Features.Projects.Ports;

namespace DevalCopilot.Application.Tests.Features.Runs.PlanningSequences;

/// <summary>A deterministic Git evidence fake for planning-sequence tests: it counts every capture, returns a configurable fingerprint
/// (so source drift is a one-line change) and can run an action or fail on a capture, exactly where a claim's external work is.</summary>
internal sealed class PlanningSequenceEvidenceReader(
    Func<Task>? onCapture = null, string? fingerprint = null, GitWorkspaceEvidenceOutcome outcome = GitWorkspaceEvidenceOutcome.Success)
    : IGitWorkspaceEvidenceReader
{
    private int captures;

    public int Captures => Volatile.Read(ref captures);

    public string Fingerprint { get; set; } = fingerprint ?? SecondChallengeRoundTestSupport.Fingerprint;

    public Func<Task>? OnCapture { get; set; } = onCapture;

    public async Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken) =>
        (await CountAsync()) with { InstructionContext = null };

    public Task<GitWorkspaceEvidenceResult> CaptureForAgentContextAsync(
        string workspacePath, bool includeUntrackedPreviews, CancellationToken cancellationToken) => CountAsync();

    private async Task<GitWorkspaceEvidenceResult> CountAsync()
    {
        Interlocked.Increment(ref captures);
        if (OnCapture is { } action)
        {
            await action();
        }

        return outcome == GitWorkspaceEvidenceOutcome.Success
            ? new GitWorkspaceEvidenceResult(
                GitWorkspaceEvidenceOutcome.Success, new string('a', 40), Fingerprint, [], null,
                InstructionContext: InstructionContextTestSupport.Delivered)
            : new GitWorkspaceEvidenceResult(outcome, null, null, [], null);
    }
}
