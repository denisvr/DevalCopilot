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

namespace DevalCopilot.Api.IntegrationTests.Features.ExecutionCapacity;

/// <summary>
/// ADR-0058's one protected MVC operation through the real host: authentication and the MVC authorization inventory, the bodyless and
/// queryless contract, the closed wire tuples (exactly four members, explicit nulls), fail-closed unavailability and that a read writes
/// nothing. Every test boots its own host and database because the reading is global to a database. Nothing here runs a provider, Git or
/// a process.
/// </summary>
public sealed class ExecutionCapacityEndpointTests : IAsyncLifetime
{
    private const string Route = "/api/execution-capacity";
    private static readonly string Fingerprint = new('a', 64);
    private static readonly string[] Members = ["limit", "recordedOwners", "status", "reasonCode"];

    private ApiWebApplicationFactory factory = null!;

    public Task InitializeAsync()
    {
        factory = new ApiWebApplicationFactory();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await factory.DisposeAsync();

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

    private sealed record Scene(Guid ProjectId, Guid RunId, Guid WorkspaceId, Guid CheckpointId, Guid RecipeId);

    private Task<Scene> SeedSceneAsync() => WithDbAsync(async db =>
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-30);
        var project = Project.Register(Guid.NewGuid(), "Execution capacity wire", $@"C:\repos\{Guid.NewGuid():N}", now);
        var run = Run.RecordClassifiedIntent(
            Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), RunExecutionMode.ManualAgent, "Disclose", now);
        run.Claim(now);
        var recipe = VerificationCommand.Configure(
            Guid.NewGuid(), project.Id, project.ReserveVerificationCommandNumber(), "Unit", @"C:\dotnet.exe", ["test"], 60, true, now);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\sentinel-{Guid.NewGuid():N}", "branch", new string('a', 40), "main", now);
        workspace.MarkReady();
        var checkpoint = GitCheckpoint.Capture(
            Guid.NewGuid(), workspace.Id, workspace.ReserveCheckpointNumber(), now, new string('a', 40), Fingerprint, []);
        db.Projects.Add(project);
        db.Runs.Add(run);
        db.VerificationCommands.Add(recipe);
        db.GitWorkspaces.Add(workspace);
        db.GitCheckpoints.Add(checkpoint);
        db.RepositoryMutationLeases.Add(RepositoryMutationLease.Acquire(
            Guid.NewGuid(), project.Id, workspace.Id, 1, Guid.NewGuid().ToByteArray(), now));
        await db.SaveChangesAsync();
        return new Scene(project.Id, run.Id, workspace.Id, checkpoint.Id, recipe.Id);
    });

    private Task<Guid> AddAgentAsync(Scene scene) => WithDbAsync(async db =>
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-20);
        var attempt = Attempt.ClaimAgentImplementation(
            Guid.NewGuid(), scene.RunId, 1, scene.WorkspaceId, scene.CheckpointId, Fingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(10),
            1024, 2048, now, 1);
        attempt.MarkAgentDispatched(now);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync();
        return attempt.Id;
    });

    private Task<Guid> AddReadOnlyAgentAsync(Scene scene) => WithDbAsync(async db =>
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-20);
        var attempt = Attempt.ClaimAgent(
            Guid.NewGuid(), scene.RunId, 1, scene.WorkspaceId, scene.CheckpointId, Fingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(10),
            1024, 2048, now, 1);
        attempt.MarkAgentDispatched(now);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync();
        return attempt.Id;
    });

    private Task<Guid> AddVerificationAsync(Scene scene) => WithDbAsync(async db =>
    {
        var workspace = await db.GitWorkspaces.AsNoTracking().SingleAsync(candidate => candidate.Id == scene.WorkspaceId);
        var checkpoint = await db.GitCheckpoints.AsNoTracking().SingleAsync(candidate => candidate.Id == scene.CheckpointId);
        var recipe = await db.VerificationCommands.AsNoTracking().SingleAsync(candidate => candidate.Id == scene.RecipeId);
        var execution = VerificationExecution.Claim(
            Guid.NewGuid(), scene.ProjectId, 1, workspace, checkpoint, recipe, DateTimeOffset.UtcNow.AddMinutes(-10));
        db.VerificationExecutions.Add(execution);
        await db.SaveChangesAsync();
        return execution.Id;
    });

    private Task<Guid> AddCommitAsync(Scene scene) => WithDbAsync(async db =>
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-15);
        var sha = new string('c', 64);
        var project = await db.Projects.SingleAsync(candidate => candidate.Id == scene.ProjectId);
        var workspace = await db.GitWorkspaces.SingleAsync(candidate => candidate.Id == scene.WorkspaceId);
        var lease = await db.RepositoryMutationLeases.AsNoTracking().SingleAsync(candidate => candidate.WorkspaceId == scene.WorkspaceId);
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
        await db.Attempts.Where(candidate => candidate.Id == attempt.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(candidate => candidate.Status, AttemptStatus.Completed));
        await db.GitWorkspaces.Where(candidate => candidate.Id == workspace.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(candidate => candidate.Status, WorkspaceStatus.Committing));
        return operationId;
    });

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private async Task<JsonElement> GetOkAsync()
    {
        using var client = Client();
        var response = await client.GetAsync(Route);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.Equal(Members.Order(StringComparer.Ordinal), body.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(2, body.GetProperty("limit").GetInt32());
        return body;
    }

    private static void AssertTuple(JsonElement body, string status, int? owners, string? reason)
    {
        Assert.Equal(status, body.GetProperty("status").GetString());
        var recorded = body.GetProperty("recordedOwners");
        if (owners is { } expected)
        {
            Assert.Equal(JsonValueKind.Number, recorded.ValueKind);
            Assert.Equal(expected, recorded.GetInt32());
        }
        else
        {
            Assert.Equal(JsonValueKind.Null, recorded.ValueKind);
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
        using var anonymous = factory.CreateClient();
        using var wrong = factory.CreateClient();
        wrong.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-secret");

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Route)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await wrong.GetAsync(Route)).StatusCode);
    }

    [Fact]
    public void Exactly_one_execution_capacity_route_exists_a_GET_that_is_authorized_and_not_anonymous()
    {
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.Contains("execution-capacity", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        var endpoint = Assert.Single(endpoints);
        Assert.Equal("api/execution-capacity", endpoint.RoutePattern.RawText);
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
        using var client = Client();

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), Route));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    // ---- the bodyless and queryless contract ----------------------------------------------------------------------------------

    [Theory]
    [InlineData("?a=1")]
    [InlineData("?limit=1")]
    [InlineData("?includeOwners=true")]
    public async Task A_query_string_is_refused_with_the_fixed_non_echoing_error(string query)
    {
        using var client = Client();

        var response = await client.GetAsync(Route + query);
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("execution_capacity.query_not_allowed", text, StringComparison.Ordinal);
        Assert.DoesNotContain(query.TrimStart('?'), text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_body_of_any_kind_is_refused_before_dispatch()
    {
        using var client = Client();

        var declared = new HttpRequestMessage(HttpMethod.Get, Route) { Content = new StringContent("{\"secret\":\"echo-me\"}") };
        var declaredResponse = await client.SendAsync(declared);
        var chunked = new HttpRequestMessage(HttpMethod.Get, Route) { Content = new StringContent(string.Empty) };
        chunked.Headers.TransferEncodingChunked = true;
        var chunkedResponse = await client.SendAsync(chunked);

        foreach (var response in new[] { declaredResponse, chunkedResponse })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var text = await response.Content.ReadAsStringAsync();
            Assert.Contains("execution_capacity.body_not_allowed", text, StringComparison.Ordinal);
            Assert.DoesNotContain("echo-me", text, StringComparison.Ordinal);
        }
    }

    // ---- the closed tuples ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_empty_database_is_Available_with_zero_recorded_owners_and_an_explicit_null_reason()
    {
        AssertTuple(await GetOkAsync(), "Available", 0, null);
    }

    [Theory]
    [InlineData("agent")]
    [InlineData("verification")]
    [InlineData("commit")]
    public async Task One_owner_of_any_family_is_Available_with_one_recorded_owner(string family)
    {
        var scene = await SeedSceneAsync();
        await AddAsync(scene, family);

        AssertTuple(await GetOkAsync(), "Available", 1, null);
    }

    [Theory]
    [InlineData("agent", "verification")]
    [InlineData("verification", "commit")]
    [InlineData("commit", "agent")]
    public async Task Two_owners_on_independent_workspaces_are_AtCapacity_with_the_fixed_reason(string first, string second)
    {
        await AddAsync(await SeedSceneAsync(), first);
        await AddAsync(await SeedSceneAsync(), second);

        AssertTuple(await GetOkAsync(), "AtCapacity", 2, "execution_capacity_limit_reached");
    }

    [Fact]
    public async Task A_third_recorded_owner_is_counted_and_never_hidden()
    {
        await AddAsync(await SeedSceneAsync(), "agent");
        await AddAsync(await SeedSceneAsync(), "verification");
        await AddAsync(await SeedSceneAsync(), "commit");

        AssertTuple(await GetOkAsync(), "AtCapacity", 3, "execution_capacity_limit_reached");
    }

    [Fact]
    public async Task A_ReadOnly_Agent_attempt_consumes_nothing()
    {
        var scene = await SeedSceneAsync();
        await AddReadOnlyAgentAsync(scene);

        AssertTuple(await GetOkAsync(), "Available", 0, null);
    }

    [Fact]
    public async Task A_terminal_owner_frees_its_place()
    {
        var first = await SeedSceneAsync();
        var second = await SeedSceneAsync();
        var firstOwner = await AddVerificationAsync(first);
        await AddAgentAsync(second);
        AssertTuple(await GetOkAsync(), "AtCapacity", 2, "execution_capacity_limit_reached");

        await WithDbAsync(async db =>
        {
            await db.VerificationExecutions.Where(candidate => candidate.Id == firstOwner)
                .ExecuteUpdateAsync(set => set.SetProperty(candidate => candidate.Status, VerificationExecutionStatus.Passed));
            return 0;
        });

        AssertTuple(await GetOkAsync(), "Available", 1, null);
    }

    [Fact]
    public async Task Unsafe_ownership_is_Unavailable_with_an_explicit_null_count_and_the_fixed_reason()
    {
        var scene = await SeedSceneAsync();
        var attemptId = await AddAgentAsync(scene);
        await WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE attempts SET AgentRole = {"ZzUnrecognized"} WHERE Id = {attemptId}");
            return 0;
        });

        var body = await GetOkAsync();

        AssertTuple(body, "Unavailable", null, "execution_capacity_unavailable");
        var text = body.GetRawText();
        Assert.DoesNotContain(scene.ProjectId.ToString(), text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(attemptId.ToString(), text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_answer_discloses_no_identifier_path_provider_detail_or_payload()
    {
        var scene = await SeedSceneAsync();
        await AddAgentAsync(scene);
        using var client = Client();

        var text = await (await client.GetAsync(Route)).Content.ReadAsStringAsync();

        Assert.DoesNotContain(scene.ProjectId.ToString(), text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(scene.WorkspaceId.ToString(), text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sentinel", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("claude", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_read_writes_nothing()
    {
        await AddAsync(await SeedSceneAsync(), "agent");
        await AddAsync(await SeedSceneAsync(), "verification");
        var before = await CountsAsync();

        await GetOkAsync();
        await GetOkAsync();

        Assert.Equal(before, await CountsAsync());
    }

    private Task<string> CountsAsync() => WithDbAsync(async db => string.Join(
        ',',
        await db.Attempts.CountAsync(),
        await db.VerificationExecutions.CountAsync(),
        await db.LocalCommitOperations.CountAsync(),
        await db.Events.CountAsync(),
        await db.Runs.CountAsync(),
        await db.Projects.SumAsync(project => project.NextVerificationExecutionNumber)));

    private async Task AddAsync(Scene scene, string family)
    {
        switch (family)
        {
            case "agent":
                await AddAgentAsync(scene);
                break;
            case "verification":
                await AddVerificationAsync(scene);
                break;
            default:
                await AddCommitAsync(scene);
                break;
        }
    }
}
