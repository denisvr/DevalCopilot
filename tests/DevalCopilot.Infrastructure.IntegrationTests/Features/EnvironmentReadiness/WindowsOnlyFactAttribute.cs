using System.Diagnostics;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.EnvironmentReadiness;

/// <summary>
/// Marks a fact as requiring Windows — specifically, NTFS directory junction support, which has
/// no equivalent this suite relies on elsewhere. xUnit v2 has no dynamic <c>Assert.Skip</c>; this
/// is the standard v2 pattern for a platform-conditional skip that still shows as genuinely
/// Skipped (not silently Passed) in the test report, using only what <see cref="FactAttribute"/>
/// already exposes — no additional test-framework package.
///
/// Being on Windows alone does not guarantee <c>mklink /J</c> actually succeeds in every
/// environment (a locked-down sandbox, a filesystem that does not support reparse points, etc.).
/// This attribute now backs its own claim with a real, one-time, cached dry-run junction
/// creation: an environment that cannot create one is marked genuinely Skipped, never silently
/// treated as satisfying whatever security assertion the test body would otherwise have made.
/// A test that needs its own junction to already exist to prove something (as opposed to one
/// that ONLY needs the capability probed) still creates it itself in its own body — this
/// attribute only decides whether the environment is capable at all.
/// </summary>
public sealed class WindowsOnlyFactAttribute : FactAttribute
{
    private static readonly Lazy<string?> UnavailableReason = new(ProbeJunctionCapability);

    public WindowsOnlyFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "This test requires Windows (NTFS directory junctions).";
            return;
        }

        if (UnavailableReason.Value is { } reason)
        {
            Skip = reason;
        }
    }

    private static string? ProbeJunctionCapability()
    {
        var probeRoot = Path.Combine(Path.GetTempPath(), $"devalcopilot-junction-capability-probe-{Guid.NewGuid():N}");
        var targetPath = Path.Combine(probeRoot, "target");
        var junctionPath = Path.Combine(probeRoot, "junction");

        try
        {
            Directory.CreateDirectory(targetPath);

            var startInfo = new ProcessStartInfo("cmd.exe")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add("mklink");
            startInfo.ArgumentList.Add("/J");
            startInfo.ArgumentList.Add(junctionPath);
            startInfo.ArgumentList.Add(targetPath);

            using var process = Process.Start(startInfo)!;
            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();

            return process.ExitCode == 0 && Directory.Exists(junctionPath)
                ? null
                : "This environment cannot create NTFS directory junctions (a dry-run `mklink /J` failed).";
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return $"This environment cannot create NTFS directory junctions ({exception.GetType().Name}).";
        }
        finally
        {
            try
            {
                if (Directory.Exists(junctionPath))
                {
                    Directory.Delete(junctionPath);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }

            try
            {
                if (Directory.Exists(probeRoot))
                {
                    Directory.Delete(probeRoot, recursive: true);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
