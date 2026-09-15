using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DevalCopilot.Api.Features.Projects.GetProjectWorkspace;
using DevalCopilot.Api.Features.Projects.PrepareRepositoryWorkspace;
using DevalCopilot.Api.Features.Projects.RecheckProjectPhysicalIdentity;
using DevalCopilot.Api.Features.Projects.RegisterProject;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Projects;

/// <summary>
/// Exercises the workspace-preparation flow end to end against the real Api host: the
/// authenticated-session requirement every endpoint shares, safe error rendering, and the
/// golden path (register a real repository, prepare a candidate workspace, inspect it).
/// </summary>
public sealed class WorkspacePreparationEndpointTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>, IDisposable
{
    private const string ProjectsRoute = "/api/projects";
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"devalcopilot-workspace-endpoint-{Guid.NewGuid():N}");

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

    private string CreateRealRepo(string name, bool dirty = false)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        RunGit(path, "init", "-q");
        RunGit(path, "config", "user.email", "test@example.com");
        RunGit(path, "config", "user.name", "Test");
        File.WriteAllText(Path.Combine(path, "file.txt"), "content");
        RunGit(path, "add", "file.txt");
        RunGit(path, "commit", "-q", "-m", "initial");

        if (dirty)
        {
            File.WriteAllText(Path.Combine(path, "file.txt"), "changed");
        }

        return path;
    }

    /// <summary>The real host's background HostCapabilityReadinessSupervisor probes Git
    /// asynchronously; registration must wait for that probe to land Ready.</summary>
    private async Task<Guid> RegisterWithRetryAsync(HttpClient client, string name, string path)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        HttpResponseMessage response;
        do
        {
            response = await client.PostAsJsonAsync(ProjectsRoute, new RegisterProjectRequest(name, path));
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var registered = await response.Content.ReadFromJsonAsync<RegisterProjectResponse>();
                return registered!.ProjectId;
            }

            var body = await response.Content.ReadAsStringAsync();
            if (!body.Contains("git_unavailable", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Registration failed unexpectedly: {response.StatusCode} {body}");
            }

            await Task.Delay(100);
        }
        while (DateTime.UtcNow < deadline);

        throw new TimeoutException("Git capability never became ready within the test deadline.");
    }

    [Fact]
    public async Task Preparing_a_workspace_without_a_credential_returns_401()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsync($"{ProjectsRoute}/{Guid.NewGuid()}/workspace/prepare", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Reading_a_workspace_without_a_credential_returns_401()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"{ProjectsRoute}/{Guid.NewGuid()}/workspace");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Rechecking_identity_without_a_credential_returns_401()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsync($"{ProjectsRoute}/{Guid.NewGuid()}/physical-identity/recheck", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_unregistered_project_returns_a_safe_not_found_for_workspace_inspection()
    {
        using var client = CreateAuthenticatedClient();

        var response = await client.GetAsync($"{ProjectsRoute}/{Guid.NewGuid()}/workspace");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_newly_registered_project_reports_not_requested_before_any_preparation()
    {
        using var client = CreateAuthenticatedClient();
        var path = CreateRealRepo("not-requested");
        var projectId = await RegisterWithRetryAsync(client, "NotRequested", path);

        var response = await client.GetAsync($"{ProjectsRoute}/{projectId}/workspace");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var workspace = await response.Content.ReadFromJsonAsync<GetProjectWorkspaceResponse>();
        Assert.Equal("NotRequested", workspace!.State);
        Assert.Null(workspace.CandidatePath);
    }

    [Fact]
    public async Task Preparing_a_workspace_for_a_clean_registered_repository_succeeds_and_is_visible_immediately()
    {
        using var client = CreateAuthenticatedClient();
        var path = CreateRealRepo("clean-prepare");
        var projectId = await RegisterWithRetryAsync(client, "CleanPrepare", path);

        var prepareResponse = await client.PostAsync($"{ProjectsRoute}/{projectId}/workspace/prepare", null);

        Assert.Equal(HttpStatusCode.OK, prepareResponse.StatusCode);
        var prepared = await prepareResponse.Content.ReadFromJsonAsync<PrepareRepositoryWorkspaceResponse>();
        Assert.True(Directory.Exists(prepared!.WorkspacePath));
        Assert.NotEqual(path, prepared.WorkspacePath);

        var workspaceResponse = await client.GetAsync($"{ProjectsRoute}/{projectId}/workspace");
        var workspace = await workspaceResponse.Content.ReadFromJsonAsync<GetProjectWorkspaceResponse>();
        Assert.Equal("Ready", workspace!.State);
        Assert.Equal(prepared.WorkspacePath, workspace.CandidatePath);
        Assert.Equal("Resolved", workspace.PhysicalIdentityStatus);
    }

    [Fact]
    public async Task Preparing_a_workspace_for_a_dirty_repository_returns_a_safe_conflict()
    {
        using var client = CreateAuthenticatedClient();
        var path = CreateRealRepo("dirty-prepare", dirty: true);
        var projectId = await RegisterWithRetryAsync(client, "DirtyPrepare", path);

        var response = await client.PostAsync($"{ProjectsRoute}/{projectId}/workspace/prepare", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("uncommitted changes", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
        Assert.DoesNotContain("System.IO", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Preparing_a_second_workspace_while_one_is_already_active_returns_a_safe_conflict()
    {
        using var client = CreateAuthenticatedClient();
        var path = CreateRealRepo("second-prepare");
        var projectId = await RegisterWithRetryAsync(client, "SecondPrepare", path);

        var first = await client.PostAsync($"{ProjectsRoute}/{projectId}/workspace/prepare", null);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PostAsync($"{ProjectsRoute}/{projectId}/workspace/prepare", null);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var body = await second.Content.ReadAsStringAsync();
        Assert.Contains("already", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Explicitly_rechecking_identity_for_a_registered_project_resolves_it()
    {
        using var client = CreateAuthenticatedClient();
        var path = CreateRealRepo("recheck-identity");
        var projectId = await RegisterWithRetryAsync(client, "RecheckIdentity", path);

        var response = await client.PostAsync($"{ProjectsRoute}/{projectId}/physical-identity/recheck", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<RecheckProjectPhysicalIdentityResponse>();
        Assert.Equal("Resolved", result!.Status);
    }
}
