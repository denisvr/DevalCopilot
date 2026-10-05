using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DevalCopilot.Api.Features.Projects.CaptureGitWorkspaceCheckpoint;
using DevalCopilot.Api.Features.Projects.PrepareRepositoryWorkspace;
using DevalCopilot.Api.Features.Projects.RegisterProject;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Projects;

/// <summary>
/// One real registered project with a real Git repository, a real prepared linked workspace and real Windows hard links, driven
/// only through the authenticated production HTTP operations of the real host. The checkpoint inspection tests mutate the
/// prepared workspace and read the response of the protected checkpoint-diff route as raw text, so they can assert about the
/// ENTIRE body and not only about the properties of one response contract.
/// </summary>
internal sealed class CheckpointInspectionScene : IDisposable
{
    internal const string Sentinel = "OUTSIDE-SENTINEL-5e91: bytes from outside the worktree.";
    private const string ProjectsRoute = "/api/projects";

    private readonly HttpClient _client;
    private readonly string _root;

    private CheckpointInspectionScene(string root, HttpClient client, Guid projectId, string workspace, string outside)
    {
        _root = root;
        _client = client;
        ProjectId = projectId;
        Workspace = workspace;
        Outside = outside;
    }

    internal Guid ProjectId { get; }

    /// <summary>The prepared linked workspace the host inspects.</summary>
    internal string Workspace { get; }

    /// <summary>A file outside every worktree that holds <see cref="Sentinel"/>.</summary>
    internal string Outside { get; }

    internal static async Task<CheckpointInspectionScene> CreateAsync(
        ApiWebApplicationFactory factory, string name, IReadOnlyDictionary<string, string> committed)
    {
        var root = Path.Combine(Path.GetTempPath(), $"devalcopilot-inspection-{Guid.NewGuid():N}");
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        var repository = Path.Combine(root, "repository");
        Directory.CreateDirectory(repository);
        Git(repository, "init", "-q");
        Git(repository, "config", "user.email", "test@example.invalid");
        Git(repository, "config", "user.name", "Test");
        Git(repository, "config", "core.autocrlf", "false");
        foreach (var (relativePath, content) in committed)
        {
            WriteText(repository, relativePath, content);
        }

        Git(repository, "add", "-A");
        Git(repository, "commit", "-q", "-m", "initial");

        var projectId = await RegisterAsync(client, name, repository);
        var prepare = await client.PostAsync($"{ProjectsRoute}/{projectId}/workspace/prepare", null);
        Assert.Equal(HttpStatusCode.OK, prepare.StatusCode);
        var prepared = await prepare.Content.ReadFromJsonAsync<PrepareRepositoryWorkspaceResponse>();
        var outside = Path.Combine(root, "outside.txt");
        File.WriteAllText(outside, Sentinel);
        var scene = new CheckpointInspectionScene(root, client, projectId, prepared!.WorkspacePath, outside);
        Directory.CreateDirectory(root);
        return scene;
    }

    internal static string WorkspaceFile(string workspace, string relativePath) =>
        Path.Combine(workspace, relativePath.Replace('/', Path.DirectorySeparatorChar));

    internal void Write(string relativePath, string content) => WriteText(Workspace, relativePath, content);

    internal void WriteBytes(string relativePath, byte[] content)
    {
        var path = WorkspaceFile(Workspace, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }

    internal void Delete(string relativePath) => File.Delete(WorkspaceFile(Workspace, relativePath));

    /// <summary>Replaces a tracked file with a second name of the outside file, exactly as the planner's mechanism evidence did.</summary>
    internal void ReplaceWithOutsideHardLink(string relativePath)
    {
        var path = WorkspaceFile(Workspace, relativePath);
        File.Delete(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Run("cmd.exe", ["/c", "mklink", "/H", path, Outside], null);
    }

    /// <summary>Gives a worktree file a second name inside the worktree.</summary>
    internal void AddInsideAlias(string relativePath, string aliasRelativePath) =>
        Run("cmd.exe", ["/c", "mklink", "/H", WorkspaceFile(Workspace, aliasRelativePath), WorkspaceFile(Workspace, relativePath)], null);

    internal void GitInWorkspace(params string[] arguments) => Git(Workspace, arguments);

    internal async Task<(Guid CheckpointId, string Fingerprint)> CaptureAsync()
    {
        var response = await _client.PostAsync($"{ProjectsRoute}/{ProjectId}/workspace/checkpoints", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var captured = await response.Content.ReadFromJsonAsync<CaptureGitWorkspaceCheckpointResponse>();
        return (captured!.CheckpointId, captured.FingerprintSha256);
    }

    internal async Task<(HttpStatusCode Status, string Body)> InspectAsync(Guid checkpointId)
    {
        var response = await _client.GetAsync($"{ProjectsRoute}/{ProjectId}/workspace/checkpoints/{checkpointId}/diff");
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    internal async Task<(HttpStatusCode Status, string Body)> InspectOfProjectAsync(Guid projectId, Guid checkpointId)
    {
        var response = await _client.GetAsync($"{ProjectsRoute}/{projectId}/workspace/checkpoints/{checkpointId}/diff");
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    internal async Task<string> ChangedFilesBodyAsync(Guid checkpointId)
    {
        var response = await _client.GetAsync($"{ProjectsRoute}/{ProjectId}/workspace/checkpoints/{checkpointId}/changed-files");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    internal static JsonElement Parse(string body) => JsonDocument.Parse(body).RootElement.Clone();

    public void Dispose()
    {
        _client.Dispose();
        if (!Directory.Exists(_root))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_root, recursive: true);
    }

    private static async Task<Guid> RegisterAsync(HttpClient client, string name, string path)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (true)
        {
            var response = await client.PostAsJsonAsync(ProjectsRoute, new RegisterProjectRequest(name, path));
            if (response.StatusCode == HttpStatusCode.OK)
            {
                return (await response.Content.ReadFromJsonAsync<RegisterProjectResponse>())!.ProjectId;
            }

            var body = await response.Content.ReadAsStringAsync();
            if (!body.Contains("git_unavailable", StringComparison.Ordinal) || DateTime.UtcNow > deadline)
            {
                throw new InvalidOperationException($"Registration failed unexpectedly: {response.StatusCode} {body}");
            }

            await Task.Delay(100);
        }
    }

    private static void WriteText(string directory, string relativePath, string content)
    {
        var path = WorkspaceFile(directory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new UTF8Encoding(false).GetBytes(content));
    }

    internal static void Git(string workingDirectory, params string[] arguments) => Run("git", arguments, workingDirectory);

    private static void Run(string fileName, IEnumerable<string> arguments, string? workingDirectory)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(fileName)
            {
                WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"{fileName} failed: {output}");
    }
}
