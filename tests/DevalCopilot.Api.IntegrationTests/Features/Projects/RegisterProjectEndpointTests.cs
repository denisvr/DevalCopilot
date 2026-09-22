using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DevalCopilot.Api.Features.Projects.GetProjectRunSummaries;
using DevalCopilot.Api.Features.Projects.RegisterProject;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Projects;

public sealed class RegisterProjectEndpointTests(HostCapabilityReadinessApiWebApplicationFactory factory)
    : IClassFixture<HostCapabilityReadinessApiWebApplicationFactory>, IDisposable
{
    private const string Route = "/api/projects";
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"devalcopilot-register-endpoint-{Guid.NewGuid():N}");

    public void Dispose()
    {
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

    private HttpClient CreateAuthenticatedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return client;
    }

    private static void RunGit(string workingDirectory, params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = workingDirectory,
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
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    private string CreateRealRepo(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        RunGit(path, "init", "-q");
        RunGit(path, "config", "user.email", "test@example.com");
        RunGit(path, "config", "user.name", "Test");
        File.WriteAllText(Path.Combine(path, "file.txt"), "content");
        RunGit(path, "add", "file.txt");
        RunGit(path, "commit", "-q", "-m", "initial");
        return path;
    }

    /// <summary>
    /// The real host's background HostCapabilityReadinessSupervisor probes Git asynchronously;
    /// registration must wait for that probe to land Ready before it can succeed, the same real
    /// race a user's first registration attempt could hit moments after launch.
    /// </summary>
    private async Task<HttpResponseMessage> RegisterWithRetryAsync(HttpClient client, string name, string path)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        HttpResponseMessage response;
        do
        {
            response = await client.PostAsJsonAsync(Route, new RegisterProjectRequest(name, path));
            if (response.StatusCode != HttpStatusCode.Conflict)
            {
                return response;
            }

            var body = await response.Content.ReadAsStringAsync();
            if (!body.Contains("git_unavailable", StringComparison.Ordinal))
            {
                return response;
            }

            await Task.Delay(100);
        }
        while (DateTime.UtcNow < deadline);

        return response;
    }

    [Fact]
    public async Task An_absent_credential_returns_401()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(Route, new RegisterProjectRequest("Name", @"C:\repos\anything"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Registering_a_real_repository_succeeds_and_is_immediately_visible_in_project_summaries()
    {
        using var client = CreateAuthenticatedClient();
        var path = CreateRealRepo("real-repo");

        var response = await RegisterWithRetryAsync(client, "Real repo", path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var registered = await response.Content.ReadFromJsonAsync<RegisterProjectResponse>();
        Assert.NotEqual(Guid.Empty, registered!.ProjectId);

        var summaries = await client.GetFromJsonAsync<List<ProjectRunSummaryResponse>>("/api/projects/run-summaries");
        var summary = summaries!.Single(s => s.ProjectId == registered.ProjectId);
        Assert.Equal(path, summary.CanonicalPath);
        Assert.Equal("OnBranch", summary.HeadState);
        Assert.False(summary.IsDirty);
        Assert.NotNull(summary.HeadCommitSha);
    }

    [Fact]
    public async Task Registering_the_same_path_twice_returns_a_safe_conflict_message()
    {
        using var client = CreateAuthenticatedClient();
        var path = CreateRealRepo("duplicate-repo");

        var first = await RegisterWithRetryAsync(client, "First", path);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await RegisterWithRetryAsync(client, "Second", path);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var body = await second.Content.ReadAsStringAsync();
        Assert.Contains("already registered", body, StringComparison.OrdinalIgnoreCase);
        // Never a raw exception, stack trace, or filesystem implementation detail.
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
        Assert.DoesNotContain("System.IO", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Registering_a_non_git_directory_returns_a_safe_not_a_repository_message()
    {
        using var client = CreateAuthenticatedClient();
        var path = Path.Combine(_root, "plain-directory");
        Directory.CreateDirectory(path);

        var response = await RegisterWithRetryAsync(client, "Plain", path);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("not a Git repository", body, StringComparison.OrdinalIgnoreCase);
    }
}
