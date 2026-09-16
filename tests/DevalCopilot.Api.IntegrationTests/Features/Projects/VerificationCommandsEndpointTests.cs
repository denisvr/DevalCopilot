using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DevalCopilot.Api.Features.Projects.ConfigureVerificationCommand;
using DevalCopilot.Api.Features.Projects.GetProjectVerificationCommands;
using DevalCopilot.Api.Features.Projects.GetProjectVerificationExecutions;
using DevalCopilot.Api.Features.Projects.UpdateVerificationCommand;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Projects;

public sealed class VerificationCommandsEndpointTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    private const string ProjectsRoute = "/api/projects";

    private HttpClient CreateAuthenticatedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return client;
    }

    private async Task<Guid> SeedProjectAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var project = Project.Register(Guid.NewGuid(), "Verification project", $@"C:\repos\{Guid.NewGuid():N}", DateTimeOffset.UtcNow);
        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        return project.Id;
    }

    [Fact]
    public async Task Configuration_endpoints_require_an_authenticated_session()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"{ProjectsRoute}/{Guid.NewGuid()}/verification-commands");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Execution_status_and_output_endpoints_require_an_authenticated_session()
    {
        using var client = factory.CreateClient();
        var projectId = Guid.NewGuid();
        var executionId = Guid.NewGuid();

        var statusResponse = await client.GetAsync($"{ProjectsRoute}/{projectId}/verification-executions");
        var outputResponse = await client.GetAsync(
            $"{ProjectsRoute}/{projectId}/verification-executions/{executionId}/output/stdout?fromOffset=0&maxBytes=1024");

        Assert.Equal(HttpStatusCode.Unauthorized, statusResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, outputResponse.StatusCode);
    }

    [Fact]
    public async Task Configure_list_update_and_delete_are_project_scoped_and_use_literal_argument_arrays()
    {
        using var client = CreateAuthenticatedClient();
        var projectId = await SeedProjectAsync();
        var route = $"{ProjectsRoute}/{projectId}/verification-commands";

        var createResponse = await client.PostAsJsonAsync(
            route,
            new ConfigureVerificationCommandRequest(
                "Backend tests",
                @"C:\Program Files\dotnet\dotnet.exe",
                ["test", "DevalCopilot.slnx", "--no-restore"],
                300,
                true));

        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var configured = await createResponse.Content.ReadFromJsonAsync<ConfigureVerificationCommandResponse>();
        Assert.NotNull(configured);

        var listResponse = await client.GetFromJsonAsync<List<VerificationCommandResponse>>(route);
        var listed = Assert.Single(listResponse!);
        Assert.Equal(["test", "DevalCopilot.slnx", "--no-restore"], listed.Arguments);
        Assert.True(listed.IsEnabled);

        var updateResponse = await client.PutAsJsonAsync(
            $"{route}/{configured!.VerificationCommandId}",
            new UpdateVerificationCommandRequest(
                "Backend tests",
                @"C:\Program Files\dotnet\dotnet.exe",
                ["test", "DevalCopilot.slnx", "--no-build"],
                120,
                false));
        Assert.Equal(HttpStatusCode.NoContent, updateResponse.StatusCode);

        var deleteResponse = await client.DeleteAsync($"{route}/{configured.VerificationCommandId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        var afterDelete = await client.GetFromJsonAsync<List<VerificationCommandResponse>>(route);
        Assert.NotNull(afterDelete);
        Assert.Empty(afterDelete);
    }

    [Fact]
    public async Task Execution_projection_marks_recovered_output_as_host_interrupted_with_unknown_truncation()
    {
        using var client = CreateAuthenticatedClient();
        var projectId = await SeedRecoveredExecutionAsync();

        var response = await client.GetFromJsonAsync<List<VerificationExecutionResponse>>(
            $"{ProjectsRoute}/{projectId}/verification-executions");

        var execution = Assert.Single(response!);
        Assert.True(execution.HasStandardOutput);
        Assert.Null(execution.StandardOutputTruncated);
        Assert.Equal("RecoveredAfterHostInterruption", execution.StandardOutputCaptureOutcome);
    }

    private async Task<Guid> SeedRecoveredExecutionAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;
        var project = Project.Register(Guid.NewGuid(), "Recovered verification project", $@"C:\repos\{Guid.NewGuid():N}", now);
        var workspace = GitWorkspace.Prepare(Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", now);
        workspace.MarkReady();
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, now, new string('a', 40), new string('b', 64), []);
        var command = VerificationCommand.Configure(Guid.NewGuid(), project.Id, 1, "Backend tests", @"C:\dotnet.exe", ["test"], 60, true, now);
        var execution = VerificationExecution.Claim(Guid.NewGuid(), project.Id, 1, workspace, checkpoint, command, now);
        execution.MarkDispatched(now);
        var artifact = VerificationOutputArtifact.Record(
            Guid.NewGuid(),
            execution.Id,
            VerificationOutputPurpose.StandardOutput,
            $@"verifications\{execution.Id}\stdout.sealed",
            "sha256:recovered",
            9,
            truncated: null,
            captureOutcome: VerificationOutputCaptureOutcome.RecoveredAfterHostInterruption,
            now);

        dbContext.Projects.Add(project);
        dbContext.GitWorkspaces.Add(workspace);
        dbContext.GitCheckpoints.Add(checkpoint);
        dbContext.VerificationCommands.Add(command);
        dbContext.RepositoryMutationLeases.Add(RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, Guid.NewGuid().ToByteArray(), now));
        dbContext.VerificationExecutions.Add(execution);
        dbContext.VerificationOutputArtifacts.Add(artifact);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        return project.Id;
    }
}
