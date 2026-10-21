using DevalCopilot.Application.Features.Projects.Ports;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionSequences;

/// <summary>A Git evidence double that reports the seeded checkpoint fingerprint, counts its captures and can report a different one.</summary>
internal sealed class LocalExecutionSequenceMatchingEvidenceReader : IGitWorkspaceEvidenceReader
{
    private int captures;

    public string Fingerprint { get; set; } = LocalExecutionSequenceApiSeed.Fingerprint;

    public int Captures => Volatile.Read(ref captures);

    public Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref captures);
        return Task.FromResult(new GitWorkspaceEvidenceResult(
            GitWorkspaceEvidenceOutcome.Success, new string('a', 40), Fingerprint, [], null));
    }
}
