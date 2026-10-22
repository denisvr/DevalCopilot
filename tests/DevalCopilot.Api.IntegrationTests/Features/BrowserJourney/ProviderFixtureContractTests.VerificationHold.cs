using System.Diagnostics;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.BrowserJourney;

// The contention journey (LCQ-V1, ADR-0062) holds one production-created verification owner with the double's owned gate: a plain file in
// the owned fixture directory. These pins keep the gate honest: it only delays, never logs while held, never changes a verdict, is released
// the moment the file is removed, and is inert when absent.
public sealed partial class ProviderFixtureContractTests
{
    private string HoldPath => Path.Combine(_root, "fixture", "verify.hold");

    private string LogFile => Path.Combine(_root, "fixture", "invocations.jsonl");

    [Fact]
    public async Task A_held_verification_waits_logs_nothing_and_finishes_with_its_own_verdict_once_the_gate_is_removed()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(HoldPath)!);
        File.WriteAllText(HoldPath, "hold");
        var info = new ProcessStartInfo(Path.Combine(_root, "bin", "verify.exe"))
        {
            WorkingDirectory = _worktree,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        info.EnvironmentVariables.Clear();
        info.ArgumentList.Add("check");
        using var process = Process.Start(info)!;
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();

        Assert.False(process.WaitForExit(1_500), "The held verification must not finish while the gate exists.");
        Assert.False(File.Exists(LogFile), "A held verification logs nothing.");

        File.Delete(HoldPath);
        Assert.True(process.WaitForExit(30_000), "The verification must finish once the gate is removed.");
        Assert.Equal(1, process.ExitCode); // the candidate is still the defective stub: the verdict is the file's alone
        Assert.Contains("FAIL", await error, StringComparison.Ordinal);
        Assert.Contains("verification started", await output, StringComparison.Ordinal);
        Assert.Single(File.ReadAllLines(LogFile));
    }

    [Fact]
    public void Without_the_gate_a_verification_is_unchanged()
    {
        Assert.False(File.Exists(HoldPath));

        var result = Run("verify", ["check"]);

        Assert.Equal(1, result.ExitCode);
    }
}
