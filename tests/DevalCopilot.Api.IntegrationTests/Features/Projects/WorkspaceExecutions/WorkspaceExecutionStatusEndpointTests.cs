using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Projects.WorkspaceExecutions;

/// <summary>
/// ADR-0057's one protected MVC operation through the real host: authentication and the MVC authorization inventory, the bodyless and
/// queryless contract, the non-disclosing 404, the closed status vocabulary with its exact tuples (explicit nulls, exactly four members),
/// the exact latest workspace, fail-closed readings, and that a read writes nothing. Nothing here runs a provider, Git or a process.
/// </summary>
public sealed class WorkspaceExecutionStatusEndpointTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    private static readonly string Fingerprint = new('a', 64);
    private static readonly string[] Members = ["projectId", "workspaceId", "status", "reasonCode"];

    private HttpClient Client()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return client;
    }

    private async Task<T> WithDbAsync<T>(Func<DevalCopilotDbContext, Task<T>> action)
    {
        using var scope = factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>());
    }

    private sealed record Seeded(Guid ProjectId, Guid RunId, Guid WorkspaceId, Guid CheckpointId, Guid RecipeId);

    private Task<Seeded> SeedAsync(bool workspace = true) => WithDbAsync(async db =>
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-30);
        var project = Project.Register(Guid.NewGuid(), "Workspace execution wire", $@"C:\repos\{Guid.NewGuid():N}", now);
        var run = Run.RecordClassifiedIntent(
            Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), RunExecutionMode.ManualAgent, "Disclose", now);
        run.Claim(now);
        var recipe = VerificationCommand.Configure(
            Guid.NewGuid(), project.Id, project.ReserveVerificationCommandNumber(), "Unit", @"C:\dotnet.exe", ["test"], 60, true, now);
        db.Projects.Add(project);
        db.Runs.Add(run);
        db.VerificationCommands.Add(recipe);
        var workspaceId = Guid.Empty;
        var checkpointId = Guid.Empty;
        if (workspace)
        {
            var prepared = GitWorkspace.Prepare(
                Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\sentinel-{Guid.NewGuid():N}", "branch", new string('a', 40), "main", now);
            prepared.MarkReady();
            var checkpoint = GitCheckpoint.Capture(
                Guid.NewGuid(), prepared.Id, prepared.ReserveCheckpointNumber(), now, new string('a', 40), Fingerprint, []);
            db.GitWorkspaces.Add(prepared);
            db.GitCheckpoints.Add(checkpoint);
            db.RepositoryMutationLeases.Add(RepositoryMutationLease.Acquire(
                Guid.NewGuid(), project.Id, prepared.Id, 1, Guid.NewGuid().ToByteArray(), now));
            workspaceId = prepared.Id;
            checkpointId = checkpoint.Id;
        }

        await db.SaveChangesAsync();
        return new Seeded(project.Id, run.Id, workspaceId, checkpointId, recipe.Id);
    });

    private Task AddAgentAttemptAsync(Seeded seed, bool anotherRun = false) => WithDbAsync(async db =>
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-20);
        var runId = seed.RunId;
        if (anotherRun)
        {
            var project = await db.Projects.SingleAsync(candidate => candidate.Id == seed.ProjectId);
            var other = Run.RecordClassifiedIntent(
                Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), RunExecutionMode.ManualAgent, "Another", now);
            other.Claim(now);
            db.Runs.Add(other);
            await db.SaveChangesAsync();
            runId = other.Id;
        }

        var attempt = Attempt.ClaimAgentImplementation(
            Guid.NewGuid(), runId, 1, seed.WorkspaceId, seed.CheckpointId, Fingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(10), 1024, 2048,
            now, 1);
        attempt.MarkAgentDispatched(now);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync();
        return 0;
    });

    private Task AddVerificationAsync(Seeded seed, int number = 1) => WithDbAsync(async db =>
    {
        var workspace = await db.GitWorkspaces.AsNoTracking().SingleAsync(candidate => candidate.Id == seed.WorkspaceId);
        var checkpoint = await db.GitCheckpoints.AsNoTracking().SingleAsync(candidate => candidate.Id == seed.CheckpointId);
        var recipe = await db.VerificationCommands.AsNoTracking().SingleAsync(candidate => candidate.Id == seed.RecipeId);
        db.VerificationExecutions.Add(VerificationExecution.Claim(
            Guid.NewGuid(), seed.ProjectId, number, workspace, checkpoint, recipe, DateTimeOffset.UtcNow.AddMinutes(-10)));
        await db.SaveChangesAsync();
        return 0;
    });

    private static string Route(Guid projectId) => $"/api/projects/{projectId}/workspace-execution-status";

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private async Task<JsonElement> GetOkAsync(Guid projectId)
    {
        using var client = Client();
        var response = await client.GetAsync(Route(projectId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.Equal(Members.Order(StringComparer.Ordinal), body.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(projectId, body.GetProperty("projectId").GetGuid());
        return body;
    }

    private static void AssertTuple(JsonElement body, string status, Guid? workspaceId, string? reason)
    {
        Assert.Equal(status, body.GetProperty("status").GetString());
        var workspace = body.GetProperty("workspaceId");
        if (workspaceId is { } expected)
        {
            Assert.Equal(expected, workspace.GetGuid());
        }
        else
        {
            Assert.Equal(JsonValueKind.Null, workspace.ValueKind);
        }

        var reasonCode = body.GetProperty("reasonCode");
        if (reason is null)
        {
            Assert.Equal(JsonValueKind.Null, reasonCode.ValueKind);
        }
        else
        {
            Assert.Equal(reason, reasonCode.GetString());
        }
    }

    // ---- authentication and the MVC authorization inventory ------------------------------------------------------------------

    [Fact]
    public async Task The_operation_requires_the_launch_session_secret()
    {
        var seed = await SeedAsync();
        using var anonymous = factory.CreateClient();
        using var wrong = factory.CreateClient();
        wrong.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-secret");

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Route(seed.ProjectId))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await wrong.GetAsync(Route(seed.ProjectId))).StatusCode);
    }

    [Fact]
    public void Exactly_one_workspace_execution_status_route_exists_a_GET_that_is_authorized_and_not_anonymous()
    {
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.Contains("workspace-execution-status", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        var endpoint = Assert.Single(endpoints);
        Assert.Equal("api/projects/{projectId:guid}/workspace-execution-status", endpoint.RoutePattern.RawText);
        Assert.Equal(["GET"], endpoint.Metadata.GetOrderedMetadata<HttpMethodMetadata>().Single().HttpMethods);
        Assert.NotEmpty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
        Assert.Empty(endpoint.Metadata.GetOrderedMetadata<IAllowAnonymous>());
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    public async Task No_mutating_method_exists_on_the_route(string method)
    {
        var seed = await SeedAsync();
        using var client = Client();

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), Route(seed.ProjectId)));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    // ---- the bodyless, queryless and non-disclosing contract -----------------------------------------------------------------

    [Fact]
    public async Task An_unknown_project_is_the_existing_non_disclosing_not_found_that_echoes_nothing()
    {
        var unknown = Guid.NewGuid();
        using var client = Client();

        var response = await client.GetAsync(Route(unknown));
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("projects.not_found", text, StringComparison.Ordinal);
        using var problem = JsonDocument.Parse(text);
        foreach (var property in problem.RootElement.EnumerateObject().Where(property => property.Name != "instance"))
        {
            Assert.DoesNotContain(unknown.ToString(), property.Value.GetRawText(), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData("?a=1")]
    [InlineData("?projectId=1")]
    [InlineData("?includeOwners=true")]
    public async Task A_query_string_is_refused_with_the_fixed_non_echoing_error(string query)
    {
        var seed = await SeedAsync();
        using var client = Client();

        var response = await client.GetAsync(Route(seed.ProjectId) + query);
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("workspace_execution.query_not_allowed", text, StringComparison.Ordinal);
        Assert.DoesNotContain(query.TrimStart('?'), text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_body_of_any_kind_is_refused_before_dispatch()
    {
        var seed = await SeedAsync();
        using var client = Client();

        var declared = new HttpRequestMessage(HttpMethod.Get, Route(seed.ProjectId)) { Content = new StringContent("{\"secret\":\"echo-me\"}") };
        var declaredResponse = await client.SendAsync(declared);
        var chunked = new HttpRequestMessage(HttpMethod.Get, Route(seed.ProjectId)) { Content = new StringContent(string.Empty) };
        chunked.Headers.TransferEncodingChunked = true;
        var chunkedResponse = await client.SendAsync(chunked);

        foreach (var response in new[] { declaredResponse, chunkedResponse })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var text = await response.Content.ReadAsStringAsync();
            Assert.Contains("workspace_execution.body_not_allowed", text, StringComparison.Ordinal);
            Assert.DoesNotContain("echo-me", text, StringComparison.Ordinal);
        }
    }

    // ---- the closed tuples ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_project_without_a_workspace_is_NoWorkspace_with_an_explicit_null_workspace()
    {
        var seed = await SeedAsync(workspace: false);

        AssertTuple(await GetOkAsync(seed.ProjectId), "NoWorkspace", null, "workspace_not_prepared");
    }

    [Fact]
    public async Task A_ready_leased_workspace_without_an_owner_is_Idle_with_the_exact_workspace_and_an_explicit_null_reason()
    {
        var seed = await SeedAsync();

        var body = await GetOkAsync(seed.ProjectId);

        AssertTuple(body, "Idle", seed.WorkspaceId, null);
    }

    [Fact]
    public async Task An_Agent_attempt_of_any_run_of_the_project_is_AgentAttemptActive()
    {
        var seed = await SeedAsync();
        await AddAgentAttemptAsync(seed, anotherRun: true);

        AssertTuple(await GetOkAsync(seed.ProjectId), "AgentAttemptActive", seed.WorkspaceId, "agent_attempt_active");
    }

    [Fact]
    public async Task A_running_verification_is_VerificationActive()
    {
        var seed = await SeedAsync();
        await AddVerificationAsync(seed);

        AssertTuple(await GetOkAsync(seed.ProjectId), "VerificationActive", seed.WorkspaceId, "verification_active");
    }

    [Fact]
    public async Task A_Committing_workspace_is_CommitReserved()
    {
        var seed = await SeedAsync();
        await WithDbAsync(async db =>
        {
            await db.GitWorkspaces.Where(candidate => candidate.Id == seed.WorkspaceId)
                .ExecuteUpdateAsync(set => set.SetProperty(candidate => candidate.Status, WorkspaceStatus.Committing));
            return 0;
        });

        AssertTuple(await GetOkAsync(seed.ProjectId), "CommitReserved", seed.WorkspaceId, "local_commit_reserved");
    }

    [Fact]
    public async Task An_Agent_attempt_beside_a_verification_is_Conflicted_without_naming_a_winner_or_an_owner()
    {
        var seed = await SeedAsync();
        await AddAgentAttemptAsync(seed, anotherRun: true);
        await AddVerificationAsync(seed);

        var body = await GetOkAsync(seed.ProjectId);

        AssertTuple(body, "Conflicted", seed.WorkspaceId, "conflicting_execution_owners");
    }

    // ---- The reciprocal association checks (WEX-R1), through the real host --------------------------------------------------

    private Task<Guid> AddOpenCommitAsync(Seeded seed) => WithDbAsync(async db =>
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-15);
        var sha = new string('c', 64);
        var project = await db.Projects.SingleAsync(candidate => candidate.Id == seed.ProjectId);
        var workspace = await db.GitWorkspaces.SingleAsync(candidate => candidate.Id == seed.WorkspaceId);
        var lease = await db.RepositoryMutationLeases.AsNoTracking().SingleAsync(candidate => candidate.WorkspaceId == seed.WorkspaceId);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), "Deliver", now);
        run.Claim(now);
        var checkpoint = GitCheckpoint.Capture(
            Guid.NewGuid(), workspace.Id, workspace.ReserveCheckpointNumber(), now, new string('a', 40), sha, []);
        var attempt = Attempt.ClaimAgentCodeReview(
            Guid.NewGuid(), run.Id, 1, Guid.NewGuid(), Guid.NewGuid(), sha, Guid.NewGuid(), TimeSpan.FromMinutes(10), 1024, 2048, now, 1);
        var agentReview = CheckpointReview.Record(
            Guid.NewGuid(), project.Id, workspace.Id, checkpoint.Id, checkpoint.CheckpointNumber, sha, ReviewActorKind.FutureAgent,
            ReviewDecision.Pending, now, []);
        var humanReview = CheckpointReview.Record(
            Guid.NewGuid(), project.Id, workspace.Id, checkpoint.Id, checkpoint.CheckpointNumber, sha, ReviewActorKind.Human,
            ReviewDecision.Pending, now, []);
        db.Runs.Add(run);
        db.GitCheckpoints.Add(checkpoint);
        db.Attempts.Add(attempt);
        db.CheckpointReviews.AddRange(agentReview, humanReview);
        await db.SaveChangesAsync();

        var operationId = Guid.NewGuid();
        var operation = LocalCommitOperation.Prepare(
            new LocalCommitOperation.PreparedFacts(
                operationId, run.Id, project.Id, workspace.Id, lease.Id, checkpoint.Id, checkpoint.CheckpointNumber, sha, attempt.Id,
                Guid.NewGuid(), Guid.NewGuid(), agentReview.Id, humanReview.Id, sha, sha, "Deliver", workspace.BranchName,
                new string('a', 40), new string('e', 40), new string('d', 40), "Local Owner", "owner@example.com", 1_800_000_000, sha, sha,
                $@"operations\{operationId:N}\prepared.index", 1, 10, now),
            [LocalCommitAuthorityMember.Record(
                Guid.NewGuid(), operationId, LocalCommitAuthorityMemberKind.HumanReview, 0, humanReview.Id, null, sha)]);
        db.LocalCommitOperations.Add(operation);
        await db.SaveChangesAsync();
        // The review attempt is the finished attempt it is in production, so the only row under judgment is the operation itself.
        await db.Attempts.Where(candidate => candidate.Id == attempt.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(candidate => candidate.Status, AttemptStatus.Completed));
        return operationId;
    });

    private Task<Guid> AddVerificationIdAsync(Seeded seed) => WithDbAsync(async db =>
    {
        var workspace = await db.GitWorkspaces.AsNoTracking().SingleAsync(candidate => candidate.Id == seed.WorkspaceId);
        var checkpoint = await db.GitCheckpoints.AsNoTracking().SingleAsync(candidate => candidate.Id == seed.CheckpointId);
        var recipe = await db.VerificationCommands.AsNoTracking().SingleAsync(candidate => candidate.Id == seed.RecipeId);
        var execution = VerificationExecution.Claim(
            Guid.NewGuid(), seed.ProjectId, 1, workspace, checkpoint, recipe, DateTimeOffset.UtcNow.AddMinutes(-10));
        db.VerificationExecutions.Add(execution);
        await db.SaveChangesAsync();
        return execution.Id;
    });

    private Task RepointVerificationAsync(Guid executionId, Guid workspaceId) => WithDbAsync(async db =>
    {
        await db.VerificationExecutions.Where(candidate => candidate.Id == executionId)
            .ExecuteUpdateAsync(set => set.SetProperty(candidate => candidate.GitWorkspaceId, workspaceId));
        return 0;
    });

    private Task RepointCommitAsync(Guid operationId, Guid workspaceId) => WithDbAsync(async db =>
    {
        await db.LocalCommitOperations.Where(candidate => candidate.Id == operationId)
            .ExecuteUpdateAsync(set => set.SetProperty(candidate => candidate.GitWorkspaceId, workspaceId));
        return 0;
    });

    [Fact]
    public async Task A_verification_of_the_project_naming_a_workspace_of_another_project_is_Unavailable()
    {
        var own = await SeedAsync();
        var other = await SeedAsync();
        await RepointVerificationAsync(await AddVerificationIdAsync(own), other.WorkspaceId);

        AssertTuple(await GetOkAsync(own.ProjectId), "Unavailable", own.WorkspaceId, "workspace_execution_unavailable");
    }

    [Fact]
    public async Task An_open_commit_of_the_project_naming_a_workspace_of_another_project_is_Unavailable()
    {
        var own = await SeedAsync();
        var other = await SeedAsync();
        await RepointCommitAsync(await AddOpenCommitAsync(own), other.WorkspaceId);

        AssertTuple(await GetOkAsync(own.ProjectId), "Unavailable", own.WorkspaceId, "workspace_execution_unavailable");
    }

    [Fact]
    public async Task A_verification_of_another_project_naming_the_projects_workspace_is_Unavailable()
    {
        var own = await SeedAsync();
        var other = await SeedAsync();
        await RepointVerificationAsync(await AddVerificationIdAsync(other), own.WorkspaceId);

        AssertTuple(await GetOkAsync(own.ProjectId), "Unavailable", own.WorkspaceId, "workspace_execution_unavailable");
    }

    [Fact]
    public async Task An_open_commit_of_another_project_naming_the_projects_workspace_is_Unavailable()
    {
        var own = await SeedAsync();
        var other = await SeedAsync();
        await RepointCommitAsync(await AddOpenCommitAsync(other), own.WorkspaceId);

        AssertTuple(await GetOkAsync(own.ProjectId), "Unavailable", own.WorkspaceId, "workspace_execution_unavailable");
    }

    [Fact]
    public async Task A_released_lease_is_Unavailable_never_Idle()
    {
        var seed = await SeedAsync();
        await WithDbAsync(async db =>
        {
            await db.RepositoryMutationLeases.Where(lease => lease.WorkspaceId == seed.WorkspaceId)
                .ExecuteUpdateAsync(set => set.SetProperty(lease => lease.Status, LeaseStatus.Released));
            return 0;
        });

        AssertTuple(await GetOkAsync(seed.ProjectId), "Unavailable", seed.WorkspaceId, "workspace_execution_unavailable");
    }

    [Fact]
    public async Task An_unrecognized_stored_attempt_status_fails_closed_without_echoing_it()
    {
        var seed = await SeedAsync();
        await AddAgentAttemptAsync(seed, anotherRun: true);
        await WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync("UPDATE attempts SET Status = 'ZzUnrecognized-sentinel'");
            return 0;
        });
        using var client = Client();

        var response = await client.GetAsync(Route(seed.ProjectId));
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("sentinel", text, StringComparison.OrdinalIgnoreCase);
        AssertTuple(await BodyAsync(response), "AgentAttemptActive", seed.WorkspaceId, "agent_attempt_active");
    }

    [Fact]
    public async Task The_latest_workspace_is_the_one_disclosed_and_the_response_carries_no_path_payload_or_owner()
    {
        var seed = await SeedAsync();
        var newer = await WithDbAsync(async db =>
        {
            var workspace = GitWorkspace.Prepare(
                Guid.NewGuid(), seed.ProjectId, 2, $@"C:\workspaces\second-{Guid.NewGuid():N}", "branch-2", new string('a', 40), "main",
                DateTimeOffset.UtcNow.AddMinutes(-5));
            workspace.MarkReady();
            db.GitWorkspaces.Add(workspace);
            db.RepositoryMutationLeases.Add(RepositoryMutationLease.Acquire(
                Guid.NewGuid(), seed.ProjectId, workspace.Id, 2, Guid.NewGuid().ToByteArray(), DateTimeOffset.UtcNow.AddMinutes(-5)));
            await db.SaveChangesAsync();
            return workspace.Id;
        });
        await AddAgentAttemptAsync(seed);
        using var client = Client();

        var response = await client.GetAsync(Route(seed.ProjectId));
        var text = await response.Content.ReadAsStringAsync();

        AssertTuple(await BodyAsync(response), "Idle", newer, null);
        Assert.DoesNotContain("workspaces", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sentinel", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_read_writes_nothing()
    {
        var seed = await SeedAsync();
        await AddVerificationAsync(seed);
        var before = await WithDbAsync(async db => (
            await db.Attempts.CountAsync(), await db.VerificationExecutions.CountAsync(), await db.Events.CountAsync(),
            await db.GitWorkspaces.CountAsync(), await db.Projects.Where(p => p.Id == seed.ProjectId).Select(p => p.NextVerificationExecutionNumber).SingleAsync()));

        await GetOkAsync(seed.ProjectId);
        await GetOkAsync(seed.ProjectId);

        var after = await WithDbAsync(async db => (
            await db.Attempts.CountAsync(), await db.VerificationExecutions.CountAsync(), await db.Events.CountAsync(),
            await db.GitWorkspaces.CountAsync(), await db.Projects.Where(p => p.Id == seed.ProjectId).Select(p => p.NextVerificationExecutionNumber).SingleAsync()));
        Assert.Equal(before, after);
    }
}
