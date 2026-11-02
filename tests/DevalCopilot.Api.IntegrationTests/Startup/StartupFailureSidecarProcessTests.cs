using System.Diagnostics;
using DevalCopilot.Api.Startup;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Startup;

/// <summary>
/// ADR-0072 acceptance D through the real production entry point as an actual child process, exactly as the shell starts it: a fatal
/// startup recovery failure writes one closed frame on the stdout protocol channel (never the Ready line), the same safe diagnostic
/// on stderr, binds nothing and exits with the documented code; a direct host writes only the stderr diagnostic. No path, message or
/// secret ever appears.
/// </summary>
public sealed class StartupFailureSidecarProcessTests : IDisposable
{
    private static readonly string ValidSecret = string.Concat(Enumerable.Repeat("0123456789abcdef", 4));
    private static readonly string ApiDllPath = typeof(Program).Assembly.Location;

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"devalcopilot-startup-sidecar-{Guid.NewGuid():N}");
    private Process? _process;

    public StartupFailureSidecarProcessTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task A_sidecar_whose_database_is_not_a_database_reports_one_closed_frame_and_never_becomes_ready()
    {
        var database = Path.Combine(_root, "garbage.db");
        await File.WriteAllBytesAsync(database, Enumerable.Repeat((byte)0x5A, 8192).ToArray());
        _process = Start(database, bootstrap: true);
        await _process.StandardInput.WriteAsync($$"""{"version":1,"secret":"{{ValidSecret}}"}""" + "\n");
        await _process.StandardInput.FlushAsync();

        var (stdout, stderr) = await ReadToExitAsync(_process);

        Assert.Equal(StartupFailureFrame.ExitCode, _process.ExitCode);
        var protocolLines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(
            ["DEVALCOPILOT_SIDECAR_FAILED {\"version\":1,\"stage\":\"StorageSchema\",\"reason\":\"StateIncoherent\",\"action\":\"RequestReviewedRepair\"}"],
            protocolLines);
        Assert.DoesNotContain("DEVALCOPILOT_SIDECAR_READY", stdout);
        Assert.Contains("Startup failed: DCSF1-StorageSchema-StateIncoherent-RequestReviewedRepair", DiagnosticLines(stderr));

        // The protocol channel carries nothing but the closed frame; ordinary provider logging stays on stderr, outside this contract.
        Assert.DoesNotContain(_root, stdout, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ValidSecret, stdout + stderr);
    }

    [Fact]
    public async Task A_direct_host_whose_database_cannot_be_opened_writes_only_the_safe_stderr_diagnostic()
    {
        var directoryAsDatabase = Path.Combine(_root, "directory.db");
        Directory.CreateDirectory(directoryAsDatabase);
        _process = Start(directoryAsDatabase, bootstrap: false);
        _process.StandardInput.Close();

        var (stdout, stderr) = await ReadToExitAsync(_process);

        Assert.Equal(StartupFailureFrame.ExitCode, _process.ExitCode);
        Assert.DoesNotContain("DEVALCOPILOT_SIDECAR_", stdout);
        Assert.Equal(["Startup failed: DCSF1-StorageSchema-StorageUnavailable-RestoreStorageAccess"], DiagnosticLines(stderr));
    }

    /// <summary>The safe diagnostic lines exactly as written: nothing but the closed code.</summary>
    private static string[] DiagnosticLines(string stderr) => stderr
        .Split(Environment.NewLine.ToCharArray(), StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(line => line.StartsWith("Startup failed:", StringComparison.Ordinal))
        .ToArray();

    private static Process Start(string databasePath, bool bootstrap)
    {
        var startInfo = new ProcessStartInfo("dotnet", $"\"{ApiDllPath}\"" + (bootstrap ? " --bootstrap-stdin" : string.Empty))
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "PlaywrightSmoke";
        startInfo.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:0";
        startInfo.Environment["ConnectionStrings__DevalCopilot"] = $"Data Source={databasePath}";
        return Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start the host process.");
    }

    private static async Task<(string Stdout, string Stderr)> ReadToExitAsync(Process process)
    {
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(deadline.Token);
        return (await stdout, await stderr);
    }

    public void Dispose()
    {
        if (_process is { HasExited: false })
        {
            _process.Kill(entireProcessTree: true);
        }

        _process?.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
