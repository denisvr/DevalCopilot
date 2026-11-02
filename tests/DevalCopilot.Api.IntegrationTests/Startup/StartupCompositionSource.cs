using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Startup;

/// <summary>
/// The host's startup composition as source text, for the order pins of the startup recoveries. ADR-0072 moved the ordered recovery
/// steps from Program.cs into <c>StartupRecovery</c>; this inlines that file at its one call site so every pin keeps reading the
/// steps in the order the host runs them, before the host itself starts.
/// </summary>
internal static class StartupCompositionSource
{
    private const string CallSite = "StartupRecovery.RunAsync(";

    public static string Read()
    {
        var api = FindApiDirectory();
        var program = File.ReadAllText(Path.Combine(api, "Program.cs"));
        var recovery = File.ReadAllText(Path.Combine(api, "Startup", "StartupRecovery.cs"));

        var callSite = program.IndexOf(CallSite, StringComparison.Ordinal);
        Assert.True(callSite > 0, "Program.cs runs the startup recovery");
        Assert.Equal(callSite, program.LastIndexOf(CallSite, StringComparison.Ordinal));
        return program[..callSite] + recovery + program[callSite..];
    }

    private static string FindApiDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "backend", "DevalCopilot.Api");
            if (File.Exists(Path.Combine(candidate, "Program.cs")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("The API project directory was not found.");
    }
}
