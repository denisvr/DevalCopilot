using DevalCopilot.Application.Features.Projects.Ports;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>Porcelain states and attested facts for the tracked-evidence tests.</summary>
internal static class TrackedFixture
{
    public static GitWorkspaceChangedPath Modified(string path) => new(path, null, " ", "M");

    public static GitWorkspaceChangedPath Staged(string path) => new(path, null, "M", " ");

    public static GitWorkspaceChangedPath Added(string path) => new(path, null, "A", " ");

    public static GitWorkspaceChangedPath Deleted(string path) => new(path, null, " ", "D");

    public static GitWorkspaceChangedPath Untracked(string path) => new(path, null, "?", "?");

    public static GitWorkspaceTrackedFile Edit(string path, string before, string after) => new(path, null, before, after);

    public static GitWorkspaceTrackedFile Add(string path, string after) => new(path, null, null, after);

    public static GitWorkspaceTrackedFile Delete(string path, string before) => new(path, null, before, null);

    public static GitWorkspaceTrackedFile Omit(string path, GitWorkspaceTrackedOmission omission) =>
        GitWorkspaceTrackedFile.Omitted(path, omission);
}
