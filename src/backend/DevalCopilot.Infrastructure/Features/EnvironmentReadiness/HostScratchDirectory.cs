namespace DevalCopilot.Infrastructure.Features.EnvironmentReadiness;

/// <summary>
/// A fixed, host-owned working directory for version probes — never a project's own
/// repository. A version probe has no reason to run inside project content, and must not
/// depend on any project existing on disk.
/// </summary>
internal static class HostScratchDirectory
{
    public static string EnsureExists()
    {
        var path = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "DevalCopilot",
            "tool-discovery-scratch");

        Directory.CreateDirectory(path);
        return path;
    }
}
