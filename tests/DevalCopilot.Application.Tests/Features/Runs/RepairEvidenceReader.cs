using DevalCopilot.Application.Features.Projects.Ports;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>A Git evidence double that counts captures (so a test can prove a refused repair did no Git work)
/// and can run a hook exactly once at the first capture — after the handler's early source and target checks and
/// before its manifest is sealed and its claim transaction opens.</summary>
internal sealed class RepairEvidenceReader(GitWorkspaceEvidenceResult result) : IGitWorkspaceEvidenceReader
{
    private int _calls;
    private bool _hookRan;

    public int Calls => Volatile.Read(ref _calls);

    public Func<CancellationToken, Task>? OnFirstCapture { get; set; }

    public static RepairEvidenceReader Matching(string fingerprintSha256) =>
        new(UntrackedManifestTestSupport.Evidence(fingerprintSha256));

    public async Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        if (!_hookRan && OnFirstCapture is { } hook)
        {
            _hookRan = true;
            await hook(cancellationToken);
        }

        return result;
    }
}
