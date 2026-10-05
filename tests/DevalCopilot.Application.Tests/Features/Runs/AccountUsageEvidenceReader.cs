using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>A Git reader that counts captures and runs one hook at the first capture, which is strictly after the handler's early checks
/// and strictly before its short claim transaction: where a concurrent request would race.</summary>
public sealed class AccountUsageEvidenceReader(GitWorkspaceEvidenceResult result, Func<CancellationToken, Task>? hook = null) : IGitWorkspaceEvidenceReader
{
    private int _calls;
    private bool _hooked;

    public int Calls => Volatile.Read(ref _calls);

    public async Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        if (hook is not null && !_hooked)
        {
            _hooked = true;
            await hook(cancellationToken);
        }

        return result;
    }
}
