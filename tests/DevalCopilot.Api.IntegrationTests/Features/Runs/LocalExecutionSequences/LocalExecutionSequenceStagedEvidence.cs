using DevalCopilot.Application.Features.Projects.Ports;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionSequences;

/// <summary>A Git evidence double whose workspace fingerprint advances by one stage each time a Claude process call finishes, exactly
/// like a real provider that edits the owned workspace; verification runs never advance it. Every capture is counted.</summary>
internal sealed class LocalExecutionSequenceStagedEvidence : IGitWorkspaceEvidenceReader
{
    public static readonly string Head = new('a', 40);

    public static readonly string[] Fingerprints = [new('a', 64), new('c', 64), new('d', 64), new('e', 64), new('f', 64)];

    private int captures;

    public int Stage { get; set; }

    public int Captures => Volatile.Read(ref captures);

    public Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref captures);
        return Task.FromResult(new GitWorkspaceEvidenceResult(
            GitWorkspaceEvidenceOutcome.Success, Head, Fingerprints[Stage],
            Stage == 0 ? [] : [new GitWorkspaceChangedPath("src/Foo.cs", null, "M", " ")], null, null, null));
    }
}
