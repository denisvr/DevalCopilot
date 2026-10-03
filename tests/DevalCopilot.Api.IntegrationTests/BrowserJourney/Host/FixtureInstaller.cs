namespace DevalCopilot.Api.IntegrationTests.BrowserJourney.Host;

/// <summary>
/// Installs the one deterministic fixture executable into the owned root's bin directory under the three names the workflow
/// launches (codex, claude, verify). The apphost locates its assembly by relative name, so the assembly and its runtime files
/// are copied beside every renamed apphost unchanged.
/// </summary>
public static class FixtureInstaller
{
    public static readonly IReadOnlyList<string> RoleNames = ["codex", "claude", "verify"];

    private static readonly string[] RuntimeFiles = ["ProviderFixture.dll", "ProviderFixture.runtimeconfig.json", "ProviderFixture.deps.json"];

    public static void Install(OwnedRootGuard root, string sourceDirectory)
    {
        var apphost = Path.Combine(sourceDirectory, "ProviderFixture.exe");
        var copies = RuntimeFiles.Select(file => (Source: Path.Combine(sourceDirectory, file), Destination: Path.Combine(root.Bin, file)))
            .Concat(RoleNames.Select(role => (Source: apphost, Destination: Path.Combine(root.Bin, role + ".exe"))))
            .ToArray();

        // Every destination, its existing ancestors and its leaf, is checked before the first file is copied.
        root.RequirePlainDestination(root.Bin);
        foreach (var copy in copies)
        {
            root.RequirePlainDestination(copy.Destination);
        }

        foreach (var copy in copies)
        {
            File.Copy(copy.Source, copy.Destination, overwrite: false);
        }
    }
}
