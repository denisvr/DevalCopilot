using System.Diagnostics;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.EnvironmentReadiness;

/// <summary>
/// Marks a fact as requiring per-directory NTFS case sensitivity, so two real directories that
/// differ only by letter case can coexist. Whether this host allows it is only knowable by trying,
/// so this probes once, in a disposable scratch directory, and sets the inherited
/// <see cref="FactAttribute.Skip"/> when it cannot — the same mechanism
/// <see cref="RequiresFileSymlinkSupportFactAttribute"/> uses, so an unsupported host shows as
/// Skipped, never as a silently passed assertion.
/// </summary>
public sealed class RequiresCaseSensitiveDirectorySupportFactAttribute : FactAttribute
{
    private static readonly Lazy<string?> UnavailableReason = new(Probe);

    public RequiresCaseSensitiveDirectorySupportFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "This test requires Windows per-directory case sensitivity.";
            return;
        }

        if (UnavailableReason.Value is { } reason)
        {
            Skip = reason;
        }
    }

    /// <summary>Enables case sensitivity on a fixture directory. Returns false when the host
    /// refuses.</summary>
    public static bool TryEnableCaseSensitivity(string emptyDirectory)
    {
        try
        {
            var startInfo = new ProcessStartInfo("fsutil.exe")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("file");
            startInfo.ArgumentList.Add("setCaseSensitiveInfo");
            startInfo.ArgumentList.Add(emptyDirectory);
            startInfo.ArgumentList.Add("enable");

            using var process = Process.Start(startInfo)!;
            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    private static string? Probe()
    {
        var probeRoot = Path.Combine(Path.GetTempPath(), $"devalcopilot-case-sensitivity-probe-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(probeRoot);
            if (!TryEnableCaseSensitivity(probeRoot))
            {
                return "This host refused to enable per-directory case sensitivity (fsutil failed).";
            }

            Directory.CreateDirectory(Path.Combine(probeRoot, "Probe"));
            Directory.CreateDirectory(Path.Combine(probeRoot, "PROBE"));
            return Directory.GetDirectories(probeRoot).Length == 2
                ? null
                : "This host enabled case sensitivity but did not keep case-distinct directories separate.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return $"This host cannot create case-distinct directories ({exception.GetType().Name}).";
        }
        finally
        {
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
