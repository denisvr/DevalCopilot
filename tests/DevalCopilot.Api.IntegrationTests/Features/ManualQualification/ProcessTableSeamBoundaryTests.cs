using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

/// <summary>The offline process-table seam has no route from the manual executable: its entry point and the launcher entry never
/// name it, so the launched session always uses the native table. The entry point is not compiled into the test assembly, so its
/// source is read from the project directory above the build output.</summary>
public sealed class ProcessTableSeamBoundaryTests
{
    private static readonly string[] SeamNames = ["processTable", "launcherProcessId", "IProcessTable", "ScriptedProcessTable"];

    [Theory]
    [InlineData("ManualQualificationProgram.cs")]
    [InlineData("LauncherEntry.cs")]
    public void The_manual_entry_never_supplies_a_process_table(string file)
    {
        var source = File.ReadAllText(Path.Combine(LauncherDirectory(), file));

        Assert.Contains("class", source, StringComparison.Ordinal);
        Assert.All(SeamNames, name => Assert.DoesNotContain(name, source, StringComparison.Ordinal));
    }

    private static string LauncherDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "ManualQualification", "Launcher");
            if (File.Exists(Path.Combine(candidate, "ManualQualificationProgram.cs")))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("The launcher source directory was not found above the build output.");
    }
}
