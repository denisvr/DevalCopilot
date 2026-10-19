using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.PlanningSequences;

/// <summary>ADR-0055's three protected MVC operations through the real host: authentication and the MVC authorization inventory, the
/// exact 2 KiB arming body, the bodyless status and stop, the fixed non-disclosing errors, the replay/conflict contract and the
/// canonical string wire for revisions, event sequences and UTC times. Nothing here runs a provider, a process or a coordinator.</summary>
public sealed class PlanningSequenceEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private static readonly string Fingerprint = new('a', 64);

    private readonly ApiWebApplicationFactory baseFactory;
    private readonly WebApplicationFactoryHolder holder;

    public PlanningSequenceEndpointTests(ApiWebApplicationFactory factory)
    {
        baseFactory = factory;
        holder = new WebApplicationFactoryHolder(factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IGitWorkspaceEvidenceReader>();
            services.AddSingleton<IGitWorkspaceEvidenceReader>(new MatchingEvidenceReader());
        })));
    }

    private sealed record WebApplicationFactoryHolder(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> Factory);

    private sealed class MatchingEvidenceReader : IGitWorkspaceEvidenceReader
    {
        public Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken) =>
            Task.FromResult(new GitWorkspaceEvidenceResult(GitWorkspaceEvidenceOutcome.Success, new string('a', 40), Fingerprint, [], null));
    }

    private HttpClient Client()
    {
        var client = holder.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return client;
    }

    private async Task<T> WithDbAsync<T>(Func<DevalCopilotDbContext, Task<T>> action)
    {
        using var scope = holder.Factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>());
    }

    private sealed record SeededRun(Guid RunId, Guid ProjectId, Guid WorkspaceId, Guid CheckpointId);

    private Task<SeededRun> SeedAsync(RunExecutionMode mode = RunExecutionMode.ManualAgent) => WithDbAsync(async db =>
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-10);
        var project = Project.Register(Guid.NewGuid(), "Planning sequence", $@"C:\repos\{Guid.NewGuid():N}", now);
        var run = mode == RunExecutionMode.Legacy
            ? Run.RecordIntent(Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), "Plan the increment", now)
            : Run.RecordClassifiedIntent(Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), mode, "Plan the increment", now);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", now);
        workspace.MarkReady();
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, workspace.ReserveCheckpointNumber(), now, new string('a', 40), Fingerprint, []);
        db.Projects.Add(project);
        db.Runs.Add(run);
        db.GitWorkspaces.Add(workspace);
        db.GitCheckpoints.Add(checkpoint);
        db.RepositoryMutationLeases.Add(RepositoryMutationLease.Acquire(
            Guid.NewGuid(), project.Id, workspace.Id, BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0), Guid.NewGuid().ToByteArray(), now));
        await db.SaveChangesAsync();
        return new SeededRun(run.Id, project.Id, workspace.Id, checkpoint.Id);
    });

    private static string ArmBody(SeededRun seed, string? fingerprint = null) =>
        JsonSerializer.Serialize(new { workspaceId = seed.WorkspaceId, checkpointId = seed.CheckpointId, fingerprintSha256 = fingerprint ?? Fingerprint });

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static string Route(Guid runId) => $"/api/runs/{runId}/planning-sequence";

    private static async Task<string> ErrorCodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("errors")[0].GetProperty("code").GetString()!;
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private async Task<(HttpResponseMessage Response, JsonElement Body)> ArmAsync(SeededRun seed)
    {
        using var client = Client();
        var response = await client.PostAsync(Route(seed.RunId), Json(ArmBody(seed)));
        return (response, await BodyAsync(response));
    }

    // ---- authentication and the MVC authorization inventory ------------------------------------------------------------------

    [Fact]
    public async Task All_three_operations_require_authentication_and_an_unauthenticated_post_changes_nothing()
    {
        var seed = await SeedAsync();
        using var anonymous = holder.Factory.CreateClient();

        var arm = await anonymous.PostAsync(Route(seed.RunId), Json(ArmBody(seed)));
        var get = await anonymous.GetAsync(Route(seed.RunId));
        var stop = await anonymous.PostAsync($"{Route(seed.RunId)}/{Guid.NewGuid()}/stop", null);

        Assert.Equal(HttpStatusCode.Unauthorized, arm.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, stop.StatusCode);
        Assert.Empty(await WithDbAsync(db => db.PlanningSequences.AsNoTracking().Where(sequence => sequence.RunId == seed.RunId).ToListAsync()));
    }

    [Fact]
    public void Exactly_three_planning_sequence_routes_exist_each_authorized_with_its_one_method_and_none_anonymous()
    {
        var endpoints = holder.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.Contains("planning-sequence", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        Assert.Equal(3, endpoints.Count);
        var routes = endpoints.ToDictionary(
            endpoint => $"{endpoint.Metadata.GetOrderedMetadata<HttpMethodMetadata>().Single().HttpMethods.Single()} {endpoint.RoutePattern.RawText}");
        Assert.Contains("POST api/runs/{runId:guid}/planning-sequence", routes.Keys);
        Assert.Contains("GET api/runs/{runId:guid}/planning-sequence", routes.Keys);
        Assert.Contains("POST api/runs/{runId:guid}/planning-sequence/{sequenceId:guid}/stop", routes.Keys);
        Assert.All(endpoints, endpoint =>
        {
            Assert.NotEmpty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
            Assert.Empty(endpoint.Metadata.GetOrderedMetadata<IAllowAnonymous>());
        });
    }

    // ---- arming -------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Arming_returns_the_recorded_receipt_with_canonical_strings_and_a_replay_returns_it_without_rearming()
    {
        var seed = await SeedAsync();

        var (first, body) = await ArmAsync(seed);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(seed.RunId, body.GetProperty("runId").GetGuid());
        Assert.Equal(seed.WorkspaceId, body.GetProperty("workspaceId").GetGuid());
        Assert.Equal(seed.CheckpointId, body.GetProperty("checkpointId").GetGuid());
        Assert.Equal(Fingerprint, body.GetProperty("fingerprintSha256").GetString());
        Assert.Equal("Armed", body.GetProperty("state").GetString());
        Assert.False(body.GetProperty("replayed").GetBoolean());
        Assert.Equal("1", body.GetProperty("revision").GetString());
        var armedAt = body.GetProperty("armedAtUtc").GetString()!;
        Assert.True(DateTimeOffset.TryParseExact(armedAt, "O", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed));
        Assert.Equal(armedAt, parsed.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture));

        var (replayResponse, replay) = await ArmAsync(seed);
        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        Assert.True(replay.GetProperty("replayed").GetBoolean());
        Assert.Equal(body.GetProperty("sequenceId").GetGuid(), replay.GetProperty("sequenceId").GetGuid());
        Assert.Equal(armedAt, replay.GetProperty("armedAtUtc").GetString());
        Assert.Single(await WithDbAsync(db => db.PlanningSequences.AsNoTracking().Where(sequence => sequence.RunId == seed.RunId).ToListAsync()));
    }

    [Fact]
    public async Task A_different_tuple_conflicts_an_unknown_run_is_a_fixed_404_and_a_legacy_run_is_refused()
    {
        var seed = await SeedAsync();
        await ArmAsync(seed);
        using var client = Client();

        var conflict = await client.PostAsync(Route(seed.RunId), Json(ArmBody(seed, new string('b', 64))));
        var unknown = await client.PostAsync(Route(Guid.NewGuid()), Json(ArmBody(seed)));
        var legacy = await SeedAsync(RunExecutionMode.Legacy);
        var refused = await client.PostAsync(Route(legacy.RunId), Json(ArmBody(legacy)));

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("planning_sequence.tuple_conflict", await ErrorCodeAsync(conflict));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("planning_sequence.not_found", await ErrorCodeAsync(unknown));
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("planning_sequence.run_not_manual", await ErrorCodeAsync(refused));
    }

    [Theory]
    [InlineData("{\"workspaceId\":\"W\",\"checkpointId\":\"C\",\"fingerprintSha256\":\"F\",\"role\":\"Planner\"}")]
    [InlineData("{\"workspaceId\":\"W\",\"checkpointId\":\"C\",\"fingerprintSha256\":\"F\",\"fingerprintSha256\":\"F\"}")]
    [InlineData("{\"WorkspaceId\":\"W\",\"checkpointId\":\"C\",\"fingerprintSha256\":\"F\"}")]
    [InlineData("{\"workspaceId\":\"W\",\"checkpointId\":\"C\"}")]
    [InlineData("{\"workspace\\u0049d\":\"W\",\"checkpointId\":\"C\",\"fingerprintSha256\":\"F\"}")]
    [InlineData("{\"workspaceId\":\"00000000-0000-0000-0000-000000000000\",\"checkpointId\":\"C\",\"fingerprintSha256\":\"F\"}")]
    [InlineData("{\"workspaceId\":\" W\",\"checkpointId\":\"C\",\"fingerprintSha256\":\"F\"}")]
    [InlineData("{\"workspaceId\":\"W\",\"checkpointId\":\"C\",\"fingerprintSha256\":\"ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789\"}")]
    [InlineData("{\"workspaceId\":\"W\",\"checkpointId\":\"C\",\"fingerprintSha256\":\"abc\"}")]
    [InlineData("{\"workspaceId\":\"W\",\"checkpointId\":\"C\",\"fingerprintSha256\":7}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("")]
    public async Task A_malformed_or_non_exact_arming_body_is_refused_before_anything_is_dispatched(string template)
    {
        var seed = await SeedAsync();
        var body = template.Replace("\"W\"", $"\"{seed.WorkspaceId}\"").Replace("\"C\"", $"\"{seed.CheckpointId}\"").Replace("\"F\"", $"\"{Fingerprint}\"")
            .Replace("\" W\"", $"\" {seed.WorkspaceId}\"");
        using var client = Client();

        var response = await client.PostAsync(Route(seed.RunId), Json(body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("planning_sequence.invalid_request", await ErrorCodeAsync(response));
        Assert.Empty(await WithDbAsync(db => db.PlanningSequences.AsNoTracking().Where(sequence => sequence.RunId == seed.RunId).ToListAsync()));
    }

    [Fact]
    public async Task A_query_key_and_a_non_json_content_type_are_refused_and_an_oversized_body_is_a_fixed_413()
    {
        var seed = await SeedAsync();
        using var client = Client();

        var query = await client.PostAsync($"{Route(seed.RunId)}?force=true", Json(ArmBody(seed)));
        var text = await client.PostAsync(Route(seed.RunId), new StringContent(ArmBody(seed), Encoding.UTF8, "text/plain"));
        var oversized = await client.PostAsync(Route(seed.RunId), Json(ArmBody(seed) + new string(' ', 3 * 1024)));

        Assert.Equal(HttpStatusCode.BadRequest, query.StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, text.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
        Assert.Equal("planning_sequence.body_too_large", await ErrorCodeAsync(oversized));
        Assert.Empty(await WithDbAsync(db => db.PlanningSequences.AsNoTracking().Where(sequence => sequence.RunId == seed.RunId).ToListAsync()));
    }

    // ---- status ---------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_status_of_an_unarmed_run_is_None_with_the_arming_availability_and_never_observes_Git()
    {
        var seed = await SeedAsync();
        using var client = Client();

        var response = await client.GetAsync(Route(seed.RunId));
        var body = await BodyAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("None", body.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("sequence").ValueKind);
        Assert.Equal("Released", body.GetProperty("manualFence").GetString());
        var arming = body.GetProperty("arming");
        Assert.True(arming.GetProperty("available").GetBoolean());
        Assert.Equal(seed.WorkspaceId, arming.GetProperty("workspaceId").GetGuid());
        Assert.Equal(seed.CheckpointId, arming.GetProperty("checkpointId").GetGuid());
        Assert.Equal(Fingerprint, arming.GetProperty("fingerprintSha256").GetString());
        Assert.Equal(JsonValueKind.String, body.GetProperty("latestEventSequence").ValueKind);
    }

    [Fact]
    public async Task The_status_of_an_armed_run_carries_the_pinned_tuple_the_closed_graph_and_the_blocked_fence()
    {
        var seed = await SeedAsync();
        var (_, armed) = await ArmAsync(seed);
        using var client = Client();

        var body = await BodyAsync(await client.GetAsync(Route(seed.RunId)));

        Assert.Equal("Armed", body.GetProperty("status").GetString());
        Assert.Equal("Blocked", body.GetProperty("manualFence").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("arming").ValueKind);
        var sequence = body.GetProperty("sequence");
        Assert.Equal(armed.GetProperty("sequenceId").GetGuid(), sequence.GetProperty("sequenceId").GetGuid());
        Assert.Equal(seed.CheckpointId, sequence.GetProperty("checkpointId").GetGuid());
        Assert.Equal("Armed", sequence.GetProperty("state").GetString());
        Assert.Equal("Armed", sequence.GetProperty("recordedState").GetString());
        Assert.False(sequence.GetProperty("observedBeforePersisted").GetBoolean());
        Assert.Equal("wait_or_stop_after_current_step", sequence.GetProperty("nextActionCode").GetString());
        Assert.Equal(5, sequence.GetProperty("maximumSteps").GetInt32());
        Assert.Equal(0, sequence.GetProperty("steps").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, sequence.GetProperty("final").ValueKind);
        Assert.Equal(JsonValueKind.String, sequence.GetProperty("revision").ValueKind);
        var names = sequence.EnumerateObject().Select(property => property.Name).ToHashSet();
        Assert.Equal(
            new HashSet<string>
            {
                "sequenceId", "workspaceId", "checkpointId", "fingerprintSha256", "state", "recordedState", "observedBeforePersisted", "reasonCode",
                "reasonText", "detailCode", "nextActionCode", "nextActionText", "revision", "armedAtUtc", "updatedAtUtc", "stopRequestedAtUtc",
                "endedAtUtc", "awaitingReconciliation", "outstandingAttemptId", "maximumSteps", "steps", "final",
            },
            names);
    }

    [Fact]
    public async Task The_status_is_bodyless_an_unknown_run_is_a_fixed_404_and_a_query_key_is_refused()
    {
        var seed = await SeedAsync();
        using var client = Client();

        var unknown = await client.GetAsync(Route(Guid.NewGuid()));
        var query = await client.GetAsync($"{Route(seed.RunId)}?x=1");
        using var withBody = new HttpRequestMessage(HttpMethod.Get, Route(seed.RunId)) { Content = Json("{}") };
        var body = await client.SendAsync(withBody);

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("planning_sequence.not_found", await ErrorCodeAsync(unknown));
        Assert.Equal(HttpStatusCode.BadRequest, query.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, body.StatusCode);
        Assert.Equal("planning_sequence.body_not_allowed", await ErrorCodeAsync(body));
    }

    // ---- stop ------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Stop_is_bound_to_the_exact_run_and_sequence_and_is_idempotent()
    {
        var seed = await SeedAsync();
        var (_, armed) = await ArmAsync(seed);
        var sequenceId = armed.GetProperty("sequenceId").GetGuid();
        var other = await SeedAsync();
        using var client = Client();

        var foreign = await client.PostAsync($"{Route(other.RunId)}/{sequenceId}/stop", null);
        var unknownRun = await client.PostAsync($"{Route(Guid.NewGuid())}/{sequenceId}/stop", null);
        var unknownSequence = await client.PostAsync($"{Route(seed.RunId)}/{Guid.NewGuid()}/stop", null);
        var first = await client.PostAsync($"{Route(seed.RunId)}/{sequenceId}/stop", null);
        var firstBody = await BodyAsync(first);
        var second = await client.PostAsync($"{Route(seed.RunId)}/{sequenceId}/stop", null);
        var secondBody = await BodyAsync(second);

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownRun.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownSequence.StatusCode);
        Assert.Equal("planning_sequence.not_found", await ErrorCodeAsync(foreign));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("Stopped", firstBody.GetProperty("state").GetString());
        Assert.True(firstBody.GetProperty("changed").GetBoolean());
        Assert.False(secondBody.GetProperty("changed").GetBoolean());
        Assert.Equal(firstBody.GetProperty("revision").GetString(), secondBody.GetProperty("revision").GetString());
        Assert.Equal(JsonValueKind.String, firstBody.GetProperty("revision").ValueKind);
        Assert.Equal(1, await WithDbAsync(db => db.Events.AsNoTracking().CountAsync(
            runEvent => runEvent.RunId == seed.RunId && runEvent.EventType == RunEventType.PlanningSequenceStopRequested)));
    }

    [Fact]
    public async Task Stop_accepts_no_body_and_no_query_and_changes_nothing_when_refused()
    {
        var seed = await SeedAsync();
        var (_, armed) = await ArmAsync(seed);
        var sequenceId = armed.GetProperty("sequenceId").GetGuid();
        using var client = Client();

        var withBody = await client.PostAsync($"{Route(seed.RunId)}/{sequenceId}/stop", Json("{\"force\":true}"));
        var withQuery = await client.PostAsync($"{Route(seed.RunId)}/{sequenceId}/stop?force=true", null);

        Assert.Equal(HttpStatusCode.BadRequest, withBody.StatusCode);
        Assert.Equal("planning_sequence.body_not_allowed", await ErrorCodeAsync(withBody));
        Assert.Equal(HttpStatusCode.BadRequest, withQuery.StatusCode);
        Assert.Equal(PlanningSequenceState.Armed,
            (await WithDbAsync(db => db.PlanningSequences.AsNoTracking().SingleAsync(sequence => sequence.RunId == seed.RunId))).State);
    }

    [Fact]
    public async Task A_manual_agent_request_for_an_armed_run_is_refused_with_the_stable_manual_control_conflict()
    {
        var seed = await SeedAsync();
        await ArmAsync(seed);
        using var client = Client();

        var planning = await client.PostAsync($"/api/runs/{seed.RunId}/agent-attempts/codex-plan", null);

        Assert.Equal(HttpStatusCode.Conflict, planning.StatusCode);
        Assert.Equal("planning_sequence.manual_control_blocked", await ErrorCodeAsync(planning));
        Assert.Empty(await WithDbAsync(db => db.Attempts.AsNoTracking().Where(attempt => attempt.RunId == seed.RunId).ToListAsync()));
    }
}
