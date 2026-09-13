using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Security;

/// <summary>
/// Exercises the real production sidecar entry point (<c>--bootstrap-stdin</c>) as an
/// actual child process, exactly as the Tauri shell will: everything here is genuine
/// process/stdio behavior, not a simulation through <c>WebApplicationFactory</c>.
/// </summary>
public sealed class BootstrapSidecarProcessTests : IDisposable
{
    // Exactly 64 lowercase hex characters, built by repetition rather than a hand-typed
    // literal so its length can't silently drift from what BootstrapFrameReader requires.
    private static readonly string ValidSecret = string.Concat(Enumerable.Repeat("0123456789abcdef", 4));
    private static readonly string ApiDllPath = typeof(Program).Assembly.Location;

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-bootstrap-{Guid.NewGuid():N}.db");
    private Process? _process;

    [Fact]
    public async Task Sidecar_binds_loopback_on_an_ephemeral_port_and_shuts_down_on_stdin_eof_without_ever_leaking_the_secret()
    {
        _process = StartSidecar();

        await _process.StandardInput.WriteAsync($$"""{"version":1,"secret":"{{ValidSecret}}"}""" + "\n");
        await _process.StandardInput.FlushAsync();

        var stdoutTask = CaptureUntilReadyAsync(_process);
        var readyLine = await stdoutTask.WaitAsync(TimeSpan.FromSeconds(30));

        var port = ParsePort(readyLine);
        Assert.NotEqual(5080, port); // never the fixed non-production dev port
        Assert.InRange(port, 1, 65535);

        using var client = new HttpClient();
        var health = await client.GetAsync($"http://127.0.0.1:{port}/health");
        Assert.Equal(System.Net.HttpStatusCode.OK, health.StatusCode);

        // EOF requests orderly shutdown: closing stdin must end the process on its own.
        _process.StandardInput.Close();
        var exited = await WaitForExitAsync(_process, TimeSpan.FromSeconds(15));
        Assert.True(exited, "The sidecar did not shut down after its stdin pipe closed.");

        var allOutput = await _process.StandardOutput.ReadToEndAsync() + await _process.StandardError.ReadToEndAsync();
        Assert.DoesNotContain(ValidSecret, allOutput);
    }

    [Fact]
    public async Task Sidecar_fails_closed_and_never_binds_when_the_bootstrap_frame_is_malformed()
    {
        _process = StartSidecar();

        await _process.StandardInput.WriteAsync("not a valid bootstrap frame\n");
        await _process.StandardInput.FlushAsync();

        var exited = await WaitForExitAsync(_process, TimeSpan.FromSeconds(15));
        Assert.True(exited, "The sidecar should exit immediately on a malformed bootstrap frame.");
        Assert.NotEqual(0, _process.ExitCode);

        var stdout = await _process.StandardOutput.ReadToEndAsync();
        Assert.DoesNotContain("DEVALCOPILOT_SIDECAR_READY", stdout);
    }

    private Process StartSidecar()
    {
        var startInfo = new ProcessStartInfo("dotnet", $"\"{ApiDllPath}\" --bootstrap-stdin")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "PlaywrightSmoke";
        startInfo.Environment["ConnectionStrings__DevalCopilot"] = $"Data Source={_databasePath}";

        return Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start the sidecar process.");
    }

    private static async Task<string> CaptureUntilReadyAsync(Process process)
    {
        while (true)
        {
            var line = await process.StandardOutput.ReadLineAsync();
            if (line is null)
            {
                throw new InvalidOperationException("The sidecar's stdout ended before a readiness line was seen.");
            }

            if (line.StartsWith("DEVALCOPILOT_SIDECAR_READY ", StringComparison.Ordinal))
            {
                return line;
            }
        }
    }

    private static int ParsePort(string readyLine)
    {
        var json = readyLine["DEVALCOPILOT_SIDECAR_READY ".Length..];
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("port").GetInt32();
    }

    private static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_process is { HasExited: false })
        {
            _process.Kill(entireProcessTree: true);
        }

        _process?.Dispose();

        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }
}
