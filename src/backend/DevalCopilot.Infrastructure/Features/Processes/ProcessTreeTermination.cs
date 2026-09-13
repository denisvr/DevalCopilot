using System.Diagnostics;

namespace DevalCopilot.Infrastructure.Features.Processes;

/// <summary>
/// Kills an owned process tree, tolerating the narrow race where the process has already
/// exited on its own between the caller observing a timeout/cancellation and this call —
/// <see cref="Process.Kill(bool)"/> throws <see cref="InvalidOperationException"/> in that
/// case, and an already-terminated tree is a successful outcome for this cleanup step, not a
/// failure to surface.
/// </summary>
internal static class ProcessTreeTermination
{
    public static void KillIfStillRunning(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }
}
