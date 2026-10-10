using System.Diagnostics;
using System.Text;

namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>The only way this launcher runs Git: a fixed argument list, no shell, no optional index locks, bounded by a timeout,
/// and a failure never carries the command's output. Used for the disposable fixture and for read-only snapshots.</summary>
public static class GitProcess
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);

    public static string Run(string workingDirectory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Git could not be started.");
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEndAsync();
        if (!process.WaitForExit(Bound))
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidOperationException("Git did not finish within its bound.");
        }

        process.WaitForExit();
        _ = error.Result;
        return process.ExitCode == 0
            ? output.Result
            : throw new InvalidOperationException($"Git exited with code {process.ExitCode}.");
    }
}
