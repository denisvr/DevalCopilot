using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionSequences;

/// <summary>ADR-0060's four protected MVC operations through the real host: authentication and the MVC authorization inventory, the
/// recorded availability and its fixed refusals, the exact 1 KiB five-member arming body refused before dispatch, the confirmation and
/// tuple conflicts, the replay contract, the canonical string wire and the bodyless stop. Nothing here runs a provider, a process or a
/// coordinator, and arming consumes and starts nothing.</summary>
public sealed class LocalExecutionSequenceEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;
    private readonly LocalExecutionSequenceMatchingEvidenceReader evidence = new();

    public LocalExecutionSequenceEndpointTests(ApiWebApplicationFactory baseFactory)
    {
        factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IGitWorkspaceEvidenceReader>();
            services.AddSingleton<IGitWorkspaceEvidenceReader>(evidence);
        }));
    }

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

    private Task<LocalExecutionSequenceApiScene> SeedAsync(
        int recipeCount = 2, RunExecutionMode mode = RunExecutionMode.ManualAgent, bool running = true, Action<Run>? configureRun = null) =>
        WithDbAsync(db => LocalExecutionSequenceApiSeed.SeedAsync(db, recipeCount, mode, running, configureRun));

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static string ArmRoute(Guid runId) => $"/api/runs/{runId}/local-execution-sequence";

    private static string AvailabilityRoute(Guid runId, string plan) => $"{ArmRoute(runId)}/availability/{plan}";

    private static string StopRoute(Guid runId, Guid sequenceId) => $"{ArmRoute(runId)}/{sequenceId}/stop";

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

    private static string ArmBody(LocalExecutionSequenceApiScene scene, string confirmation, string? fingerprint = null) =>
        JsonSerializer.Serialize(new
        {
            planProposalMessageId = scene.PlanId,
            workspaceId = scene.WorkspaceId,
            checkpointId = scene.CheckpointId,
            fingerprintSha256 = fingerprint ?? LocalExecutionSequenceApiSeed.Fingerprint,
            confirmationSha256 = confirmation,
        });

    private async Task<JsonElement> AvailabilityAsync(LocalExecutionSequenceApiScene scene)
    {
        using var client = Client();
        var response = await client.GetAsync(AvailabilityRoute(scene.RunId, scene.PlanId.ToString()));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await BodyAsync(response);
    }

    private async Task<(HttpResponseMessage Response, JsonElement Body, string Confirmation)> ArmAsync(LocalExecutionSequenceApiScene scene)
    {
        var confirmation = (await AvailabilityAsync(scene)).GetProperty("confirmationSha256").GetString()!;
        using var client = Client();
        var response = await client.PostAsync(ArmRoute(scene.RunId), Json(ArmBody(scene, confirmation)));
        return (response, await BodyAsync(response), confirmation);
    }

    private Task<List<LocalExecutionSequence>> SequencesAsync(Guid runId) =>
        WithDbAsync(db => db.LocalExecutionSequences.AsNoTracking().Where(sequence => sequence.RunId == runId).ToListAsync());

    // ---- authentication and the MVC authorization inventory ------------------------------------------------------------------

    [Fact]
    public async Task All_four_operations_require_authentication_and_an_unauthenticated_post_changes_nothing()
    {
        var scene = await SeedAsync();
        using var anonymous = factory.CreateClient();

        var arm = await anonymous.PostAsync(ArmRoute(scene.RunId), Json(ArmBody(scene, new string('c', 64))));
        var status = await anonymous.GetAsync(ArmRoute(scene.RunId));
        var availability = await anonymous.GetAsync(AvailabilityRoute(scene.RunId, scene.PlanId.ToString()));
        var stop = await anonymous.PostAsync(StopRoute(scene.RunId, Guid.NewGuid()), null);

        Assert.Equal(HttpStatusCode.Unauthorized, arm.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, status.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, availability.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, stop.StatusCode);
        Assert.Empty(await SequencesAsync(scene.RunId));
    }

    [Fact]
    public void Exactly_twelve_local_execution_sequence_routes_exist_each_authorized_with_its_one_method_and_none_anonymous()
    {
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.Contains("local-execution-sequence", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        Assert.Equal(12, endpoints.Count);
        var routes = endpoints.Select(
            endpoint => $"{endpoint.Metadata.GetOrderedMetadata<HttpMethodMetadata>().Single().HttpMethods.Single()} {endpoint.RoutePattern.RawText}")
            .ToHashSet();
        Assert.Equal(
            new HashSet<string>
            {
                "GET api/runs/{runId:guid}/local-execution-sequence/availability/{planProposalMessageId}",
                "POST api/runs/{runId:guid}/local-execution-sequence",
                "GET api/runs/{runId:guid}/local-execution-sequence",
                "POST api/runs/{runId:guid}/local-execution-sequence/{sequenceId:guid}/stop",
                "POST api/runs/{runId:guid}/local-execution-sequence/queue",
                "GET api/runs/{runId:guid}/local-execution-sequence/queue/availability",
                "GET api/runs/{runId:guid}/local-execution-sequence/inspection",
                "GET api/runs/{runId:guid}/local-execution-sequence/inspection/admissions",
                "GET api/runs/{runId:guid}/local-execution-sequence/inspection/steps/{stepId:guid}",
                "GET api/runs/{runId:guid}/local-execution-sequence/inspection/steps/{stepId:guid}/artifact-window/{stream}",
                "GET api/runs/{runId:guid}/local-execution-sequence/steps/{stepId:guid}/ownership-release",
                "POST api/runs/{runId:guid}/local-execution-sequence/steps/{stepId:guid}/ownership-release",
            },
            routes);
        Assert.All(endpoints, endpoint =>
        {
            Assert.NotEmpty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
            Assert.Empty(endpoint.Metadata.GetOrderedMetadata<IAllowAnonymous>());
        });
    }

    // ---- availability -------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_availability_of_an_eligible_plan_carries_the_exact_candidate_and_the_bounded_recipe_disclosure()
    {
        var scene = await SeedAsync(configureRun: run =>
        {
            run.SetTokenStopThreshold(AgentProvider.Codex, 999999999999);
            run.SetTokenStopThreshold(AgentProvider.ClaudeCode, 1_000_000_000_000L);
            run.SetRequestedClaudeMaxTurns(7);
        });
        var capturesBefore = evidence.Captures;
        using var client = Client();

        var response = await client.GetAsync(AvailabilityRoute(scene.RunId, scene.PlanId.ToString()));
        var text = await response.Content.ReadAsStringAsync();
        var second = await BodyAsync(await client.GetAsync(AvailabilityRoute(scene.RunId, scene.PlanId.ToString())));
        using var document = JsonDocument.Parse(text);
        var body = document.RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(body.GetProperty("available").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("refusalCode").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("refusalText").ValueKind);
        Assert.Equal(scene.RunId, body.GetProperty("runId").GetGuid());
        Assert.Equal(scene.PlanId, body.GetProperty("planProposalMessageId").GetGuid());
        Assert.Equal("AcceptedRoot", body.GetProperty("planAuthority").GetString());
        Assert.Equal(scene.WorkspaceId, body.GetProperty("workspaceId").GetGuid());
        Assert.Equal(scene.CheckpointId, body.GetProperty("checkpointId").GetGuid());
        Assert.Equal(LocalExecutionSequenceApiSeed.Fingerprint, body.GetProperty("fingerprintSha256").GetString());
        Assert.Equal(2, body.GetProperty("correctionAllowance").GetInt32());
        Assert.Equal(12, body.GetProperty("maximumSteps").GetInt32());
        var confirmation = body.GetProperty("confirmationSha256").GetString()!;
        Assert.Matches("^[0-9a-f]{64}$", confirmation);
        Assert.Equal(confirmation, second.GetProperty("confirmationSha256").GetString());

        var recipes = body.GetProperty("recipes");
        Assert.Equal(2, recipes.GetArrayLength());
        for (var index = 0; index < 2; index++)
        {
            var recipe = recipes[index];
            Assert.Equal(index, recipe.GetProperty("ordinal").GetInt32());
            Assert.Equal(scene.RecipeIds[index], recipe.GetProperty("commandId").GetGuid());
            Assert.Equal(index + 1, recipe.GetProperty("commandNumber").GetInt32());
            Assert.Equal($"Unit tests {index + 1}", recipe.GetProperty("name").GetString());
            Assert.Equal(60, recipe.GetProperty("timeoutSeconds").GetInt32());
            Assert.Equal(
                new HashSet<string> { "ordinal", "commandId", "commandNumber", "name", "timeoutSeconds" },
                recipe.EnumerateObject().Select(property => property.Name).ToHashSet());
        }

        var disclosure = body.GetProperty("disclosure");
        Assert.Equal(JsonValueKind.String, disclosure.GetProperty("codexTokenStopThreshold").ValueKind);
        Assert.Equal("999999999999", disclosure.GetProperty("codexTokenStopThreshold").GetString());
        Assert.Equal("1000000000000", disclosure.GetProperty("claudeTokenStopThreshold").GetString());
        Assert.Equal(7, disclosure.GetProperty("claudeMaxTurns").GetInt32());

        Assert.DoesNotContain("SENTINEL_EXECUTABLE", text, StringComparison.Ordinal);
        Assert.DoesNotContain(LocalExecutionSequenceApiSeed.SentinelArgument, text, StringComparison.Ordinal);
        Assert.DoesNotContain(scene.WorkspacePath, text.Replace("\\\\", "\\"), StringComparison.Ordinal);
        Assert.DoesNotContain(scene.RepositoryPath, text.Replace("\\\\", "\\"), StringComparison.Ordinal);
        Assert.Equal(capturesBefore, evidence.Captures);
        Assert.Empty(await SequencesAsync(scene.RunId));
    }

    [Fact]
    public async Task The_confirmation_changes_when_the_verification_set_changes()
    {
        var scene = await SeedAsync();
        var before = (await AvailabilityAsync(scene)).GetProperty("confirmationSha256").GetString();

        await WithDbAsync(async db =>
        {
            db.VerificationCommands.Add(VerificationCommand.Configure(
                Guid.NewGuid(), scene.ProjectId, 9, "Added check", LocalExecutionSequenceApiSeed.SentinelExecutable, [], 30, true,
                DateTimeOffset.UtcNow.AddMinutes(-5)));
            await db.SaveChangesAsync();
            return 0;
        });

        var after = await AvailabilityAsync(scene);
        Assert.NotEqual(before, after.GetProperty("confirmationSha256").GetString());
        Assert.Equal(3, after.GetProperty("recipes").GetArrayLength());
        Assert.Equal(15, after.GetProperty("maximumSteps").GetInt32());
    }

    private static readonly IReadOnlyDictionary<string, Func<Devalente.Shared.Results.Error>> Refusals =
        new Dictionary<string, Func<Devalente.Shared.Results.Error>>
        {
            ["run_not_manual"] = LocalExecutionSequenceErrors.RunNotManual,
            ["run_not_running"] = LocalExecutionSequenceErrors.RunNotRunning,
            ["prior_mutation"] = LocalExecutionSequenceErrors.PriorMutation,
            ["already_armed"] = LocalExecutionSequenceErrors.AlreadyArmed,
            ["plan_not_found"] = LocalExecutionSequenceErrors.PlanNotFound,
            ["plan_not_eligible"] = LocalExecutionSequenceErrors.PlanNotEligible,
            ["project_busy"] = LocalExecutionSequenceErrors.ProjectBusy,
            ["workspace_not_ready"] = LocalExecutionSequenceErrors.WorkspaceNotReady,
            ["lease_not_active"] = LocalExecutionSequenceErrors.LeaseNotActive,
            ["checkpoint_missing"] = LocalExecutionSequenceErrors.CheckpointMissing,
            ["recipes_missing"] = LocalExecutionSequenceErrors.RecipesMissing,
            ["recipes_too_many"] = LocalExecutionSequenceErrors.RecipesTooMany,
        };

    [Theory]
    [InlineData("run_not_manual")]
    [InlineData("run_not_running")]
    [InlineData("prior_mutation")]
    [InlineData("already_armed")]
    [InlineData("plan_not_found")]
    [InlineData("plan_not_eligible")]
    [InlineData("project_busy")]
    [InlineData("workspace_not_ready")]
    [InlineData("lease_not_active")]
    [InlineData("checkpoint_missing")]
    [InlineData("recipes_missing")]
    [InlineData("recipes_too_many")]
    public async Task Each_fixed_refusal_is_one_code_with_its_fixed_text_and_no_candidate(string name)
    {
        var scene = name switch
        {
            "run_not_manual" => await SeedAsync(mode: RunExecutionMode.Legacy),
            "run_not_running" => await SeedAsync(running: false),
            "recipes_missing" => await SeedAsync(recipeCount: 0),
            "recipes_too_many" => await SeedAsync(recipeCount: 33),
            _ => await SeedAsync(),
        };
        var plan = scene.PlanId;
        switch (name)
        {
            case "already_armed":
                await ArmAsync(scene);
                break;
            case "plan_not_found":
                plan = Guid.NewGuid();
                break;
            case "plan_not_eligible":
                plan = await WithDbAsync(db => db.CollaborationMessages.AsNoTracking()
                    .Where(message => message.RunId == scene.RunId && message.Type == CollaborationMessageType.Acceptance)
                    .Select(message => message.Id).SingleAsync());
                break;
            default:
                await ShapeRefusalAsync(name, scene);
                break;
        }

        using var client = Client();
        var response = await client.GetAsync(AvailabilityRoute(scene.RunId, plan.ToString()));
        var body = await BodyAsync(response);

        var expected = Refusals[name]();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(body.GetProperty("available").GetBoolean());
        Assert.Equal($"local_execution_sequence.{name}", body.GetProperty("refusalCode").GetString());
        Assert.Equal(expected.Code, body.GetProperty("refusalCode").GetString());
        Assert.Equal(expected.Description, body.GetProperty("refusalText").GetString());
        foreach (var member in new[]
                 {
                     "planAuthority", "workspaceId", "checkpointId", "fingerprintSha256", "confirmationSha256", "correctionAllowance",
                     "maximumSteps", "recipes", "disclosure",
                 })
        {
            Assert.Equal(JsonValueKind.Null, body.GetProperty(member).ValueKind);
        }
    }

    private Task ShapeRefusalAsync(string name, LocalExecutionSequenceApiScene scene) => WithDbAsync(async db =>
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-9);
        switch (name)
        {
            case "prior_mutation":
                db.Attempts.Add(Attempt.ClaimAgent(
                    Guid.NewGuid(), scene.RunId, 3, scene.WorkspaceId, scene.CheckpointId, LocalExecutionSequenceApiSeed.Fingerprint,
                    Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, now, 3));
                break;
            case "project_busy":
                var project = await db.Projects.SingleAsync(candidate => candidate.Id == scene.ProjectId);
                var other = Run.RecordClassifiedIntent(
                    Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), RunExecutionMode.ManualAgent, "Other run", now);
                other.Claim(now);
                db.Runs.Add(other);
                db.Attempts.Add(Attempt.ClaimAgent(
                    Guid.NewGuid(), other.Id, 1, scene.WorkspaceId, scene.CheckpointId, LocalExecutionSequenceApiSeed.Fingerprint,
                    Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, now, 1));
                break;
            case "workspace_not_ready":
            case "lease_not_active":
            case "checkpoint_missing":
                var latest = GitWorkspace.Prepare(
                    Guid.NewGuid(), scene.ProjectId, 2, $@"C:\workspaces\{Guid.NewGuid():N}", "branch-two", new string('a', 40), "main", now);
                if (name == "workspace_not_ready")
                {
                    latest.MarkFailedToPrepare("workspace_prepare_failed");
                }
                else
                {
                    latest.MarkReady();
                }

                db.GitWorkspaces.Add(latest);
                if (name == "checkpoint_missing")
                {
                    db.RepositoryMutationLeases.Add(RepositoryMutationLease.Acquire(
                        Guid.NewGuid(), scene.ProjectId, latest.Id, BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0),
                        Guid.NewGuid().ToByteArray(), now));
                }

                break;
        }

        await db.SaveChangesAsync();
        return 0;
    });

    [Fact]
    public async Task A_malformed_plan_segment_is_the_fixed_400_and_an_unknown_run_the_fixed_404()
    {
        var scene = await SeedAsync();
        using var client = Client();

        foreach (var segment in new[]
                 {
                     "not-a-guid", "00000000-0000-0000-0000-000000000000", scene.PlanId.ToString("N"), $"%7B{scene.PlanId}%7D", $"%20{scene.PlanId}",
                 })
        {
            var response = await client.GetAsync(AvailabilityRoute(scene.RunId, segment));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(LocalExecutionSequenceErrors.InvalidRequestCode, await ErrorCodeAsync(response));
        }

        var unknown = await client.GetAsync(AvailabilityRoute(Guid.NewGuid(), scene.PlanId.ToString()));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(LocalExecutionSequenceErrors.NotFoundCode, await ErrorCodeAsync(unknown));
    }

    [Fact]
    public async Task The_availability_is_bodyless_and_queryless()
    {
        var scene = await SeedAsync();
        var route = AvailabilityRoute(scene.RunId, scene.PlanId.ToString());
        using var client = Client();

        var query = await client.GetAsync($"{route}?x=1");
        using var withBody = new HttpRequestMessage(HttpMethod.Get, route) { Content = Json("{}") };
        var body = await client.SendAsync(withBody);
        using var chunked = new HttpRequestMessage(HttpMethod.Get, route) { Content = Json("{}") };
        chunked.Headers.TransferEncodingChunked = true;
        var transfer = await client.SendAsync(chunked);

        Assert.Equal(HttpStatusCode.BadRequest, query.StatusCode);
        Assert.Equal(LocalExecutionSequenceErrors.InvalidRequestCode, await ErrorCodeAsync(query));
        Assert.Equal(HttpStatusCode.BadRequest, body.StatusCode);
        Assert.Equal(LocalExecutionSequenceErrors.BodyNotAllowedCode, await ErrorCodeAsync(body));
        Assert.Equal(HttpStatusCode.BadRequest, transfer.StatusCode);
        Assert.Equal(LocalExecutionSequenceErrors.BodyNotAllowedCode, await ErrorCodeAsync(transfer));
    }

    // ---- arming -------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Arming_returns_the_recorded_receipt_with_canonical_strings_and_consumes_and_starts_nothing()
    {
        var scene = await SeedAsync();
        var before = await WithDbAsync(async db => (
            Attempts: await db.Attempts.AsNoTracking().CountAsync(attempt => attempt.RunId == scene.RunId),
            Run: await db.Runs.AsNoTracking().SingleAsync(run => run.Id == scene.RunId)));

        var (response, body, confirmation) = await ArmAsync(scene);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(scene.RunId, body.GetProperty("runId").GetGuid());
        Assert.Equal(scene.PlanId, body.GetProperty("planProposalMessageId").GetGuid());
        Assert.Equal(scene.WorkspaceId, body.GetProperty("workspaceId").GetGuid());
        Assert.Equal(scene.CheckpointId, body.GetProperty("checkpointId").GetGuid());
        Assert.Equal(LocalExecutionSequenceApiSeed.Fingerprint, body.GetProperty("fingerprintSha256").GetString());
        Assert.Equal(confirmation, body.GetProperty("confirmationSha256").GetString());
        Assert.Equal("Armed", body.GetProperty("state").GetString());
        Assert.Equal(2, body.GetProperty("recipeCount").GetInt32());
        Assert.Equal(2, body.GetProperty("correctionAllowance").GetInt32());
        Assert.False(body.GetProperty("replayed").GetBoolean());
        Assert.Equal(JsonValueKind.String, body.GetProperty("revision").ValueKind);
        Assert.Equal("1", body.GetProperty("revision").GetString());
        var armedAt = body.GetProperty("armedAtUtc").GetString()!;
        Assert.True(DateTimeOffset.TryParseExact(armedAt, "O", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed));
        Assert.Equal(armedAt, parsed.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        var deadline = body.GetProperty("advancementDeadlineUtc").GetString()!;
        Assert.True(DateTimeOffset.TryParseExact(deadline, "O", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedDeadline));
        Assert.Equal(deadline, parsedDeadline.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        Assert.Equal(TimeSpan.FromHours(2), parsedDeadline - parsed);
        Assert.Equal(
            new HashSet<string>
            {
                "sequenceId", "runId", "planProposalMessageId", "workspaceId", "checkpointId", "fingerprintSha256", "confirmationSha256",
                "state", "recipeCount", "correctionAllowance", "armedAtUtc", "advancementDeadlineUtc", "revision", "replayed", "admissionMode", "queue",
            },
            body.EnumerateObject().Select(property => property.Name).ToHashSet());

        var after = await WithDbAsync(async db => new
        {
            Attempts = await db.Attempts.AsNoTracking().CountAsync(attempt => attempt.RunId == scene.RunId),
            Steps = await db.LocalExecutionSequenceSteps.AsNoTracking().CountAsync(),
            Recipes = await db.LocalExecutionSequenceRecipes.AsNoTracking().CountAsync(recipe => recipe.SequenceId == body.GetProperty("sequenceId").GetGuid()),
            Executions = await db.VerificationExecutions.AsNoTracking().CountAsync(execution => execution.ProjectId == scene.ProjectId),
            Events = await db.Events.AsNoTracking().Where(runEvent => runEvent.RunId == scene.RunId && runEvent.EventType.StartsWith("local_execution_sequence."))
                .Select(runEvent => runEvent.EventType).ToListAsync(),
            Run = await db.Runs.AsNoTracking().SingleAsync(run => run.Id == scene.RunId),
        });
        Assert.Equal(before.Attempts, after.Attempts);
        Assert.Equal(0, after.Steps);
        Assert.Equal(2, after.Recipes);
        Assert.Equal(0, after.Executions);
        Assert.Equal([RunEventType.LocalExecutionSequenceArmed], after.Events);
        Assert.Equal(before.Run.Lifecycle, after.Run.Lifecycle);
        Assert.Equal(before.Run.MaximumAgentAttempts, after.Run.MaximumAgentAttempts);
        Assert.Equal(before.Run.MaximumReviewCorrectionAttempts, after.Run.MaximumReviewCorrectionAttempts);
    }

    [Fact]
    public async Task An_identical_replay_returns_the_recorded_receipt_even_after_the_sequence_ended_and_a_different_tuple_conflicts()
    {
        var scene = await SeedAsync();
        var (_, armed, confirmation) = await ArmAsync(scene);
        var sequenceId = armed.GetProperty("sequenceId").GetGuid();
        using var client = Client();

        var replay = await client.PostAsync(ArmRoute(scene.RunId), Json(ArmBody(scene, confirmation)));
        var replayBody = await BodyAsync(replay);
        var stop = await client.PostAsync(StopRoute(scene.RunId, sequenceId), null);
        var afterEnd = await client.PostAsync(ArmRoute(scene.RunId), Json(ArmBody(scene, confirmation)));
        var afterEndBody = await BodyAsync(afterEnd);
        var conflict = await client.PostAsync(ArmRoute(scene.RunId), Json(ArmBody(scene, confirmation, new string('b', 64))));
        var otherConfirmation = await client.PostAsync(ArmRoute(scene.RunId), Json(ArmBody(scene, new string('d', 64))));

        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.True(replayBody.GetProperty("replayed").GetBoolean());
        Assert.Equal(sequenceId, replayBody.GetProperty("sequenceId").GetGuid());
        Assert.Equal(armed.GetProperty("armedAtUtc").GetString(), replayBody.GetProperty("armedAtUtc").GetString());
        Assert.Equal(HttpStatusCode.OK, stop.StatusCode);
        Assert.Equal(HttpStatusCode.OK, afterEnd.StatusCode);
        Assert.True(afterEndBody.GetProperty("replayed").GetBoolean());
        Assert.Equal(sequenceId, afterEndBody.GetProperty("sequenceId").GetGuid());
        Assert.Equal(armed.GetProperty("armedAtUtc").GetString(), afterEndBody.GetProperty("armedAtUtc").GetString());
        Assert.Equal(armed.GetProperty("advancementDeadlineUtc").GetString(), afterEndBody.GetProperty("advancementDeadlineUtc").GetString());
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(LocalExecutionSequenceErrors.TupleConflictCode, await ErrorCodeAsync(conflict));
        Assert.Equal(HttpStatusCode.Conflict, otherConfirmation.StatusCode);
        Assert.Equal(LocalExecutionSequenceErrors.TupleConflictCode, await ErrorCodeAsync(otherConfirmation));
        Assert.Single(await SequencesAsync(scene.RunId));
        Assert.Equal(1, await WithDbAsync(db => db.Events.AsNoTracking().CountAsync(
            runEvent => runEvent.RunId == scene.RunId && runEvent.EventType == RunEventType.LocalExecutionSequenceArmed)));
    }

    [Fact]
    public async Task A_stale_tuple_a_stale_confirmation_an_unknown_run_and_an_unknown_plan_are_fixed_refusals_that_record_nothing()
    {
        var scene = await SeedAsync();
        var confirmation = (await AvailabilityAsync(scene)).GetProperty("confirmationSha256").GetString()!;
        using var client = Client();

        var staleFingerprint = await client.PostAsync(ArmRoute(scene.RunId), Json(ArmBody(scene, confirmation, new string('b', 64))));
        var staleCheckpoint = await client.PostAsync(
            ArmRoute(scene.RunId),
            Json(ArmBody(scene, confirmation).Replace(scene.CheckpointId.ToString(), Guid.NewGuid().ToString(), StringComparison.Ordinal)));
        var staleWorkspace = await client.PostAsync(
            ArmRoute(scene.RunId),
            Json(ArmBody(scene, confirmation).Replace(scene.WorkspaceId.ToString(), Guid.NewGuid().ToString(), StringComparison.Ordinal)));
        var drift = await client.PostAsync(ArmRoute(scene.RunId), Json(ArmBody(scene, new string('e', 64))));
        var unknownRun = await client.PostAsync(ArmRoute(Guid.NewGuid()), Json(ArmBody(scene, confirmation)));
        var unknownPlan = await client.PostAsync(
            ArmRoute(scene.RunId),
            Json(ArmBody(scene, confirmation).Replace(scene.PlanId.ToString(), Guid.NewGuid().ToString(), StringComparison.Ordinal)));

        foreach (var stale in new[] { staleFingerprint, staleCheckpoint, staleWorkspace })
        {
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.Equal(LocalExecutionSequenceErrors.TupleStaleCode, await ErrorCodeAsync(stale));
        }

        Assert.Equal(HttpStatusCode.Conflict, drift.StatusCode);
        Assert.Equal(LocalExecutionSequenceErrors.ConfirmationStaleCode, await ErrorCodeAsync(drift));
        Assert.Equal(HttpStatusCode.NotFound, unknownRun.StatusCode);
        Assert.Equal(LocalExecutionSequenceErrors.NotFoundCode, await ErrorCodeAsync(unknownRun));
        Assert.Equal(HttpStatusCode.Conflict, unknownPlan.StatusCode);
        Assert.Equal(LocalExecutionSequenceErrors.PlanNotFoundCode, await ErrorCodeAsync(unknownPlan));
        Assert.Empty(await SequencesAsync(scene.RunId));
    }

    [Fact]
    public async Task A_configuration_change_between_the_availability_and_the_arming_is_a_confirmation_conflict()
    {
        var scene = await SeedAsync();
        var confirmation = (await AvailabilityAsync(scene)).GetProperty("confirmationSha256").GetString()!;
        await WithDbAsync(async db =>
        {
            db.VerificationCommands.Add(VerificationCommand.Configure(
                Guid.NewGuid(), scene.ProjectId, 7, "Late check", LocalExecutionSequenceApiSeed.SentinelExecutable, [], 30, true,
                DateTimeOffset.UtcNow.AddMinutes(-5)));
            await db.SaveChangesAsync();
            return 0;
        });
        using var client = Client();

        var response = await client.PostAsync(ArmRoute(scene.RunId), Json(ArmBody(scene, confirmation)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(LocalExecutionSequenceErrors.ConfirmationStaleCode, await ErrorCodeAsync(response));
        Assert.Empty(await SequencesAsync(scene.RunId));
    }

    [Fact]
    public async Task A_git_fingerprint_that_no_longer_matches_is_a_stale_tuple()
    {
        var scene = await SeedAsync();
        var confirmation = (await AvailabilityAsync(scene)).GetProperty("confirmationSha256").GetString()!;
        using var client = Client();
        evidence.Fingerprint = new string('f', 64);
        try
        {
            var response = await client.PostAsync(ArmRoute(scene.RunId), Json(ArmBody(scene, confirmation)));

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal(LocalExecutionSequenceErrors.TupleStaleCode, await ErrorCodeAsync(response));
            Assert.Empty(await SequencesAsync(scene.RunId));
        }
        finally
        {
            evidence.Fingerprint = LocalExecutionSequenceApiSeed.Fingerprint;
        }
    }

    [Theory]
    [InlineData("{\"planProposalMessageId\":\"P\",\"workspaceId\":\"W\",\"checkpointId\":\"C\",\"fingerprintSha256\":\"F\",\"confirmationSha256\":\"K\",\"command\":\"x\"}")]
    [InlineData("{\"planProposalMessageId\":\"P\",\"workspaceId\":\"W\",\"checkpointId\":\"C\",\"fingerprintSha256\":\"F\",\"confirmationSha256\":\"K\",\"provider\":\"Codex\"}")]
    [InlineData("{\"planProposalMessageId\":\"P\",\"workspaceId\":\"W\",\"checkpointId\":\"C\",\"fingerprintSha256\":\"F\",\"confirmationSha256\":\"K\",\"permission\":\"all\"}")]
    [InlineData("{\"planProposalMessageId\":\"P\",\"workspaceId\":\"W\",\"checkpointId\":\"C\",\"fingerprintSha256\":\"F\",\"confirmationSha256\":\"K\",\"budget\":5}")]
    [InlineData("{\"planProposalMessageId\":\"P\",\"workspaceId\":\"W\",\"checkpointId\":\"C\",\"fingerprintSha256\":\"F\",\"confirmationSha256\":\"K\",\"childId\":\"W\"}")]
    [InlineData("{\"planProposalMessageId\":\"P\",\"workspaceId\":\"W\",\"checkpointId\":\"C\",\"fingerprintSha256\":\"F\",\"confirmationSha256\":\"K\",\"guidance\":\"go\"}")]
    [InlineData("{\"planProposalMessageId\":\"P\",\"workspaceId\":\"W\",\"checkpointId\":\"C\",\"fingerprintSha256\":\"F\",\"confirmationSha256\":\"K\",\"confirmationSha256\":\"K\"}")]
    [InlineData("{\"PlanProposalMessageId\":\"P\",\"workspaceId\":\"W\",\"checkpointId\":\"C\",\"fingerprintSha256\":\"F\",\"confirmationSha256\":\"K\"}")]
    [InlineData("{\"planProposalMessageId\":\"P\",\"workspaceId\":\"W\",\"checkpointId\":\"C\",\"fingerprintSha256\":\"F\",\"CONFIRMATIONSHA256\":\"K\"}")]
    [InlineData("{\"planProposalMessageId\":\"P\",\"workspace\\u0049d\":\"W\",\"checkpointId\":\"C\",\"fingerprintSha256\":\"F\",\"confirmationSha256\":\"K\"}")]
    [InlineData("{\"planProposalMessageId\":\"P\",\"workspaceId\":\"W\",\"checkpointId\":\"C\",\"fingerprintSha256\":\"F\"}")]
    [InlineData("{\"workspaceId\":\"W\",\"checkpointId\":\"C\",\"fingerprintSha256\":\"F\",\"confirmationSha256\":\"K\"}")]
    [InlineData("{\"planProposalMessageId\":7,\"workspaceId\":\"W\",\"checkpointId\":\"C\",\"fingerprintSha256\":\"F\",\"confirmationSha256\":\"K\"}")]
    [InlineData("{\"planProposalMessageId\":\"P\",\"workspaceId\":\"W\",\"checkpointId\":\"C\",\"fingerprintSha256\":7,\"confirmationSha256\":\"K\"}")]
    [InlineData("{\"planProposalMessageId\":\"P\",\"workspaceId\":\"W\",\"checkpointId\":\"C\",\"fingerprintSha256\":\"F\",\"confirmationSha256\":null}")]
    [InlineData("{\"planProposalMessageId\":\"P\",\"workspaceId\":\"W\",\"checkpointId\":\"C\",\"fingerprintSha256\":\"F\",\"confirmationSha256\":true}")]
    [InlineData("{\"planProposalMessageId\":\"P\",\"workspaceId\":\"W\",\"checkpointId\":\"C\",\"fingerprintSha256\":\"F\",\"confirmationSha256\":{\"a\":1}}")]
    [InlineData("{\"planProposalMessageId\":\"P\",\"workspaceId\":\"W\",\"checkpointId\":\"C\",\"fingerprintSha256\":\"ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789\",\"confirmationSha256\":\"K\"}")]
    [InlineData("{\"planProposalMessageId\":\"P\",\"workspaceId\":\"W\",\"checkpointId\":\"C\",\"fingerprintSha256\":\"F\",\"confirmationSha256\":\"ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789\"}")]
    [InlineData("{\"planProposalMessageId\":\"P\",\"workspaceId\":\"W\",\"checkpointId\":\"C\",\"fingerprintSha256\":\"abc\",\"confirmationSha256\":\"K\"}")]
    [InlineData("{\"planProposalMessageId\":\"P\",\"workspaceId\":\"W\",\"checkpointId\":\"C\",\"fingerprintSha256\":\"F\",\"confirmationSha256\":\"abc\"}")]
    [InlineData("{\"planProposalMessageId\":\"00000000-0000-0000-0000-000000000000\",\"workspaceId\":\"W\",\"checkpointId\":\"C\",\"fingerprintSha256\":\"F\",\"confirmationSha256\":\"K\"}")]
    [InlineData("{\"planProposalMessageId\":\"P\",\"workspaceId\":\"00000000-0000-0000-0000-000000000000\",\"checkpointId\":\"C\",\"fingerprintSha256\":\"F\",\"confirmationSha256\":\"K\"}")]
    [InlineData("{\"planProposalMessageId\":\"P\",\"workspaceId\":\"W\",\"checkpointId\":\"00000000-0000-0000-0000-000000000000\",\"fingerprintSha256\":\"F\",\"confirmationSha256\":\"K\"}")]
    [InlineData("{\"planProposalMessageId\":\"P\",\"workspaceId\":\" W\",\"checkpointId\":\"C\",\"fingerprintSha256\":\"F\",\"confirmationSha256\":\"K\"}")]
    [InlineData("{\"planProposalMessageId\":\"P\",\"workspaceId\":\"{W}\",\"checkpointId\":\"C\",\"fingerprintSha256\":\"F\",\"confirmationSha256\":\"K\"}")]
    [InlineData("{\"planProposalMessageId\":\"P\",\"workspaceId\":\"W\",\"checkpointId\":\"C\",\"fingerprintSha256\":\"F\",\"confirmationSha256\":\"K\"} trailing")]
    [InlineData("{\"planProposalMessageId\":\"P\",\"workspaceId\":\"W\",\"checkpointId\":\"C\",\"fingerprintSha256\":\"F\",\"confirmationSha256\":\"K\",}")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("\"text\"")]
    [InlineData("")]
    public async Task A_malformed_or_non_exact_arming_body_is_refused_before_anything_is_dispatched(string template)
    {
        var scene = await SeedAsync();
        var confirmation = (await AvailabilityAsync(scene)).GetProperty("confirmationSha256").GetString()!;
        var body = template
            .Replace("\"P\"", $"\"{scene.PlanId}\"")
            .Replace("\"W\"", $"\"{scene.WorkspaceId}\"")
            .Replace("\"C\"", $"\"{scene.CheckpointId}\"")
            .Replace("\"F\"", $"\"{LocalExecutionSequenceApiSeed.Fingerprint}\"")
            .Replace("\"K\"", $"\"{confirmation}\"")
            .Replace("\" W\"", $"\" {scene.WorkspaceId}\"")
            .Replace("\"{W}\"", $"\"{{{scene.WorkspaceId}}}\"");
        var capturesBefore = evidence.Captures;
        using var client = Client();

        var response = await client.PostAsync(ArmRoute(scene.RunId), Json(body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(LocalExecutionSequenceErrors.InvalidRequestCode, await ErrorCodeAsync(response));
        Assert.Equal(capturesBefore, evidence.Captures);
        Assert.Empty(await SequencesAsync(scene.RunId));
        Assert.Equal(0, await WithDbAsync(db => db.Events.AsNoTracking().CountAsync(
            runEvent => runEvent.RunId == scene.RunId && runEvent.EventType == RunEventType.LocalExecutionSequenceArmed)));
    }

    [Fact]
    public async Task A_query_key_and_a_non_json_content_type_are_refused_and_an_oversized_body_is_a_fixed_413()
    {
        var scene = await SeedAsync();
        var confirmation = (await AvailabilityAsync(scene)).GetProperty("confirmationSha256").GetString()!;
        var valid = ArmBody(scene, confirmation);
        var capturesBefore = evidence.Captures;
        using var client = Client();

        var query = await client.PostAsync($"{ArmRoute(scene.RunId)}?force=true", Json(valid));
        var text = await client.PostAsync(ArmRoute(scene.RunId), new StringContent(valid, Encoding.UTF8, "text/plain"));
        var oversized = await client.PostAsync(ArmRoute(scene.RunId), Json(valid + new string(' ', 1025 - valid.Length)));
        var wayOver = await client.PostAsync(ArmRoute(scene.RunId), Json(valid + new string(' ', 4096)));

        Assert.Equal(HttpStatusCode.BadRequest, query.StatusCode);
        Assert.Equal(LocalExecutionSequenceErrors.InvalidRequestCode, await ErrorCodeAsync(query));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, text.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
        Assert.Equal(LocalExecutionSequenceErrors.BodyTooLargeCode, await ErrorCodeAsync(oversized));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, wayOver.StatusCode);
        Assert.Equal(LocalExecutionSequenceErrors.BodyTooLargeCode, await ErrorCodeAsync(wayOver));
        Assert.Equal(capturesBefore, evidence.Captures);
        Assert.Empty(await SequencesAsync(scene.RunId));
    }

    [Fact]
    public async Task A_valid_body_of_exactly_one_kibibyte_is_within_the_bound()
    {
        var scene = await SeedAsync();
        var confirmation = (await AvailabilityAsync(scene)).GetProperty("confirmationSha256").GetString()!;
        var valid = ArmBody(scene, confirmation);
        using var client = Client();

        var response = await client.PostAsync(ArmRoute(scene.RunId), Json(valid + new string(' ', 1024 - valid.Length)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(await SequencesAsync(scene.RunId));
    }

    // ---- stop -------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Stop_before_any_claim_ends_the_sequence_Stopped_with_zero_consumption_and_is_idempotent_and_exactly_bound()
    {
        var scene = await SeedAsync();
        var (_, armed, _) = await ArmAsync(scene);
        var sequenceId = armed.GetProperty("sequenceId").GetGuid();
        var other = await SeedAsync();
        using var client = Client();

        var foreign = await client.PostAsync(StopRoute(other.RunId, sequenceId), null);
        var unknownRun = await client.PostAsync(StopRoute(Guid.NewGuid(), sequenceId), null);
        var unknownSequence = await client.PostAsync(StopRoute(scene.RunId, Guid.NewGuid()), null);
        var first = await client.PostAsync(StopRoute(scene.RunId, sequenceId), null);
        var firstBody = await BodyAsync(first);
        var second = await client.PostAsync(StopRoute(scene.RunId, sequenceId), null);
        var secondBody = await BodyAsync(second);

        foreach (var refusal in new[] { foreign, unknownRun, unknownSequence })
        {
            Assert.Equal(HttpStatusCode.NotFound, refusal.StatusCode);
            Assert.Equal(LocalExecutionSequenceErrors.NotFoundCode, await ErrorCodeAsync(refusal));
        }

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("Stopped", firstBody.GetProperty("state").GetString());
        Assert.Equal("stopped_before_claim", firstBody.GetProperty("reasonCode").GetString());
        Assert.True(firstBody.GetProperty("changed").GetBoolean());
        Assert.Equal(JsonValueKind.String, firstBody.GetProperty("revision").ValueKind);
        Assert.Equal(sequenceId, firstBody.GetProperty("sequenceId").GetGuid());
        Assert.Equal(scene.RunId, firstBody.GetProperty("runId").GetGuid());
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.False(secondBody.GetProperty("changed").GetBoolean());
        Assert.Equal("Stopped", secondBody.GetProperty("state").GetString());
        Assert.Equal(firstBody.GetProperty("revision").GetString(), secondBody.GetProperty("revision").GetString());
        Assert.Equal(1, await WithDbAsync(db => db.Events.AsNoTracking().CountAsync(
            runEvent => runEvent.RunId == scene.RunId && runEvent.EventType == RunEventType.LocalExecutionSequenceStopRequested)));
        Assert.Equal(1, await WithDbAsync(db => db.Events.AsNoTracking().CountAsync(
            runEvent => runEvent.RunId == scene.RunId && runEvent.EventType == RunEventType.LocalExecutionSequenceEnded)));
        Assert.Equal(2, await WithDbAsync(db => db.Attempts.AsNoTracking().CountAsync(attempt => attempt.RunId == scene.RunId)));
        Assert.Equal(0, await WithDbAsync(db => db.LocalExecutionSequenceSteps.AsNoTracking().CountAsync(step => step.SequenceId == sequenceId)));
    }

    [Fact]
    public async Task Stop_accepts_no_body_and_no_query_and_changes_nothing_when_refused()
    {
        var scene = await SeedAsync();
        var (_, armed, _) = await ArmAsync(scene);
        var sequenceId = armed.GetProperty("sequenceId").GetGuid();
        using var client = Client();

        var withBody = await client.PostAsync(StopRoute(scene.RunId, sequenceId), Json("{\"force\":true}"));
        var withQuery = await client.PostAsync($"{StopRoute(scene.RunId, sequenceId)}?force=true", null);

        Assert.Equal(HttpStatusCode.BadRequest, withBody.StatusCode);
        Assert.Equal(LocalExecutionSequenceErrors.BodyNotAllowedCode, await ErrorCodeAsync(withBody));
        Assert.Equal(HttpStatusCode.BadRequest, withQuery.StatusCode);
        Assert.Equal(LocalExecutionSequenceErrors.InvalidRequestCode, await ErrorCodeAsync(withQuery));
        Assert.Equal(LocalExecutionSequenceState.Armed, (await SequencesAsync(scene.RunId)).Single().State);
    }
}
