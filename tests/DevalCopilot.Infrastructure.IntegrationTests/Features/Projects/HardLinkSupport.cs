using System.Diagnostics;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Projects;

/// <summary>Creates a REAL NTFS hard link (<c>mklink /H</c>, which needs no privilege) so a regression can name one physical
/// file by two paths. Never skipped: the host that runs these tests must be able to make one.</summary>
internal static class HardLinkSupport
{
    internal static void Create(string linkPath, string existingFilePath)
    {
        var startInfo = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/H");
        startInfo.ArgumentList.Add(linkPath);
        startInfo.ArgumentList.Add(existingFilePath);
        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"mklink /H failed: {output}");
    }
}
