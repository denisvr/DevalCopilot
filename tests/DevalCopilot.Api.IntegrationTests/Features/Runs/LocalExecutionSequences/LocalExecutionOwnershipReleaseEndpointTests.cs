using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;
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

/// <summary>
/// ADR-0065's two protected MVC operations through the real host: authentication and the MVC authorization inventory, the closed wire
/// (only an Available read carries a candidate and only a Released one a receipt; counters and times are canonical strings and nothing of
/// the private provenance is exposed), the fixed error vocabulary, the bodyless GET and the exact 512-byte four-string POST refused before
/// anything is dispatched, exact replay and a single receipt and event. No provider, process or coordinator runs here.
/// </summary>
public sealed class LocalExecutionOwnershipReleaseEndpointTests : IDisposable
{
    // One host and database per test: a quarantined mutating owner of one scenario must never count against another's capacity.
    private readonly ApiWebApplicationFactory baseFactory = new();
    private readonly WebApplicationFactory<Program> factory;
    private readonly LocalExecutionSequenceMatchingEvidenceReader evidence = new();

    public LocalExecutionOwnershipReleaseEndpointTests()
    {
        factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IGitWorkspaceEvidenceReader>();
            services.AddSingleton<IGitWorkspaceEvidenceReader>(evidence);
        }));
    }

    public void Dispose()
    {
        factory.Dispose();
        baseFactory.Dispose();
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

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static string Route(Guid runId, Guid stepId) => $"/api/runs/{runId}/local-execution-sequence/steps/{stepId}/ownership-release";

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static async Task<string> ErrorCodeAsync(HttpResponseMessage response) =>
        (await BodyAsync(response)).GetProperty("errors")[0].GetProperty("code").GetString()!;

    /// <summary>An armed API scene turned into an ended cycle with one terminal timed-out implementation step.</summary>
    private async Task<(LocalExecutionSequenceApiScene Scene, LocalExecutionOwnershipReleaseApiSeed.Terminal Terminal)> TerminalAsync(
        bool witnessed = true, bool observed = true)
    {
        var scene = await WithDbAsync(db => LocalExecutionSequenceApiSeed.SeedAsync(db));
        using var client = Client();
        var availability = await client.GetAsync($"/api/runs/{scene.RunId}/local-execution-sequence/availability/{scene.PlanId}");
        var confirmation = (await BodyAsync(availability)).GetProperty("confirmationSha256").GetString()!;
        var arm = await client.PostAsync(
            $"/api/runs/{scene.RunId}/local-execution-sequence",
            Json(JsonSerializer.Serialize(new
            {
                planProposalMessageId = scene.PlanId,
                workspaceId = scene.WorkspaceId,
                checkpointId = scene.CheckpointId,
                fingerprintSha256 = LocalExecutionSequenceApiSeed.Fingerprint,
                confirmationSha256 = confirmation,
            })));
        Assert.True(arm.StatusCode == HttpStatusCode.OK, await arm.Content.ReadAsStringAsync());
        var terminal = await WithDbAsync(db => LocalExecutionOwnershipReleaseApiSeed.SeedTimedOutAsync(db, scene.RunId, witnessed, observed));
        return (scene, terminal);
    }

    private static string ReleaseBody(JsonElement candidate, string? sha = null, string? expires = null) => JsonSerializer.Serialize(new
    {
        observationId = candidate.GetProperty("observationId").GetString(),
        confirmationVersion = candidate.GetProperty("confirmationVersion").GetString(),
        confirmationSha256 = sha ?? candidate.GetProperty("confirmationSha256").GetString(),
        confirmationExpiresAtUtc = expires ?? candidate.GetProperty("confirmationExpiresAtUtc").GetString(),
    });

    // ---- authentication and the MVC authorization inventory --------------------------------------------------------------------

    [Fact]
    public async Task Both_operations_require_authentication_and_an_unauthenticated_post_changes_nothing()
    {
        var (scene, terminal) = await TerminalAsync();
        using var anonymous = factory.CreateClient();

        var read = await anonymous.GetAsync(Route(scene.RunId, terminal.StepId));
        var post = await anonymous.PostAsync(Route(scene.RunId, terminal.StepId), Json("{}"));

        Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, post.StatusCode);
        Assert.Equal(0, await WithDbAsync(db => db.LocalExecutionOwnershipReleases.CountAsync()));
    }

    [Fact]
    public void The_two_ownership_release_routes_exist_each_authorized_with_its_one_method_and_none_anonymous()
    {
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.Contains("ownership-release", StringComparison.OrdinalIgnoreCase) == true
                && !endpoint.RoutePattern.RawText.Contains("/inspection/", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(2, endpoints.Count);
        var routes = endpoints.Select(
            endpoint => $"{endpoint.Metadata.GetOrderedMetadata<HttpMethodMetadata>().Single().HttpMethods.Single()} {endpoint.RoutePattern.RawText}")
            .ToHashSet();
        Assert.Equal(
            new HashSet<string>
            {
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

    [Fact]
    public void The_real_host_composes_one_host_launch_identity_for_every_handler()
    {
        var first = factory.Services.GetRequiredService<LocalExecutionHostLaunch>();
        using var scope = factory.Services.CreateScope();

        Assert.Same(first, scope.ServiceProvider.GetRequiredService<LocalExecutionHostLaunch>());
    }

    // ---- the closed read ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_available_read_is_the_closed_graph_with_canonical_strings_and_none_of_the_private_provenance()
    {
        var (scene, terminal) = await TerminalAsync();
        using var client = Client();

        var response = await client.GetAsync(Route(scene.RunId, terminal.StepId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.Equal(
            ["runId", "stepId", "status", "reasonCode", "candidate", "release"],
            body.EnumerateObject().Select(property => property.Name));
        Assert.Equal("Available", body.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("reasonCode").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("release").ValueKind);
        var candidate = body.GetProperty("candidate");
        Assert.Equal(
            [
                "sequenceId", "sequenceState", "admissionMode", "sourceCheckpointId", "sourceFingerprintSha256", "stepKind", "ordinal", "passNumber",
                "operationFamily", "operationId", "operationStatus", "processOutcome", "semanticExitCode", "observationId", "physicalExitCode",
                "observedElapsedTicks", "processDurationTicks", "dispatchedAtUtc", "observedAtUtc", "confirmationVersion", "confirmationSha256",
                "confirmationExpiresAtUtc",
            ],
            candidate.EnumerateObject().Select(property => property.Name));
        Assert.Equal("NeedsAttention", candidate.GetProperty("sequenceState").GetString());
        Assert.Equal("Immediate", candidate.GetProperty("admissionMode").GetString());
        Assert.Equal("Implementation", candidate.GetProperty("stepKind").GetString());
        Assert.Equal("Agent", candidate.GetProperty("operationFamily").GetString());
        Assert.Equal("Failed", candidate.GetProperty("operationStatus").GetString());
        Assert.Equal("TimedOut", candidate.GetProperty("processOutcome").GetString());
        Assert.Equal(JsonValueKind.Null, candidate.GetProperty("semanticExitCode").ValueKind);
        Assert.Equal(137, candidate.GetProperty("physicalExitCode").GetInt32());
        Assert.Equal(TimeSpan.FromSeconds(4).Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture), candidate.GetProperty("observedElapsedTicks").GetString());
        Assert.Equal(TimeSpan.FromSeconds(5).Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture), candidate.GetProperty("processDurationTicks").GetString());
        Assert.Equal("1", candidate.GetProperty("confirmationVersion").GetString());
        Assert.True(LocalExecutionOwnershipReleaseConfirmation.TryParseTime(candidate.GetProperty("confirmationExpiresAtUtc").GetString(), out var expiry));
        Assert.True(expiry - DateTimeOffset.UtcNow <= TimeSpan.FromMinutes(2));
        Assert.True(LocalExecutionOwnershipReleaseConfirmation.TryParseTime(candidate.GetProperty("dispatchedAtUtc").GetString(), out _));
        Assert.True(LocalExecutionOwnershipReleaseConfirmation.TryParseTime(candidate.GetProperty("observedAtUtc").GetString(), out _));

        var raw = await response.Content.ReadAsStringAsync();
        foreach (var forbidden in new[] { "nonce", "hostLaunch", "ticket", "processId", "path", "arguments", "environment", "stdout", "stderr" })
        {
            Assert.DoesNotContain(forbidden, raw, StringComparison.OrdinalIgnoreCase);
        }

        var witness = await WithDbAsync(db => db.LocalExecutionDispatchWitnesses.AsNoTracking().SingleAsync());
        Assert.DoesNotContain(witness.Nonce.ToString(), raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(witness.HostLaunchId.ToString(), raw, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false, false, "Unavailable", "evidence_missing")]
    [InlineData(true, false, "Unavailable", "evidence_missing")]
    public async Task A_missing_witness_or_observation_is_the_closed_unavailable_graph_with_no_candidate_and_no_release(
        bool witnessed, bool observed, string status, string reason)
    {
        var (scene, terminal) = await TerminalAsync(witnessed, observed);
        using var client = Client();

        var body = await BodyAsync(await client.GetAsync(Route(scene.RunId, terminal.StepId)));

        Assert.Equal(status, body.GetProperty("status").GetString());
        Assert.Equal(reason, body.GetProperty("reasonCode").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("candidate").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("release").ValueKind);
    }

    [Fact]
    public async Task An_unknown_run_or_step_is_one_fixed_not_found_that_repeats_no_value()
    {
        var (scene, terminal) = await TerminalAsync();
        var other = await TerminalAsync();
        using var client = Client();
        var foreign = Guid.NewGuid();

        var responses = new[]
        {
            await client.GetAsync(Route(foreign, terminal.StepId)),
            await client.GetAsync(Route(scene.RunId, foreign)),
            await client.GetAsync(Route(scene.RunId, other.Terminal.StepId)),
        };

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
        foreach (var response in responses)
        {
            // The problem document's standard instance member is the request path; no error member repeats a value.
            var problem = await BodyAsync(response);
            var error = problem.GetProperty("errors")[0];
            Assert.Equal(LocalExecutionOwnershipReleaseErrors.NotFoundCode, error.GetProperty("code").GetString());
            var errorText = error.ToString() + problem.GetProperty("title").GetString() + problem.GetProperty("detail").GetString();
            Assert.DoesNotContain(foreign.ToString(), errorText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(terminal.StepId.ToString(), errorText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(scene.RunId.ToString(), errorText, StringComparison.OrdinalIgnoreCase);
        }

        var post = await client.PostAsync(Route(foreign, terminal.StepId), Json("{}"));
        Assert.True(post.StatusCode is HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_read_is_bodyless_and_queryless_with_the_existing_fixed_refusals()
    {
        var (scene, terminal) = await TerminalAsync();
        using var client = Client();

        var query = await client.GetAsync($"{Route(scene.RunId, terminal.StepId)}?refresh=true");
        var body = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, Route(scene.RunId, terminal.StepId)) { Content = Json("{}") });

        Assert.Equal(HttpStatusCode.BadRequest, query.StatusCode);
        Assert.Equal("local_execution_inspection.query_not_allowed", await ErrorCodeAsync(query));
        Assert.Equal(HttpStatusCode.BadRequest, body.StatusCode);
        Assert.Equal("local_execution_inspection.body_not_allowed", await ErrorCodeAsync(body));
    }

    // ---- the release ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_post_with_the_exact_confirmation_returns_the_released_graph_replays_it_and_a_changed_tuple_conflicts()
    {
        var (scene, terminal) = await TerminalAsync();
        using var client = Client();
        var candidate = (await BodyAsync(await client.GetAsync(Route(scene.RunId, terminal.StepId)))).GetProperty("candidate");
        var body = ReleaseBody(candidate);

        var released = await client.PostAsync(Route(scene.RunId, terminal.StepId), Json(body));

        Assert.Equal(HttpStatusCode.OK, released.StatusCode);
        var graph = await BodyAsync(released);
        Assert.Equal(["runId", "stepId", "status", "reasonCode", "candidate", "release"], graph.EnumerateObject().Select(property => property.Name));
        Assert.Equal("Released", graph.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, graph.GetProperty("candidate").ValueKind);
        var release = graph.GetProperty("release");
        Assert.Equal(["releaseId", "operationFamily", "operationId", "stepKind", "releasedAtUtc", "eventSequence"], release.EnumerateObject().Select(property => property.Name));
        Assert.Equal("Agent", release.GetProperty("operationFamily").GetString());
        Assert.Equal(terminal.AttemptId, release.GetProperty("operationId").GetGuid());
        Assert.True(long.Parse(release.GetProperty("eventSequence").GetString()!, System.Globalization.CultureInfo.InvariantCulture) >= 1);
        Assert.True(LocalExecutionOwnershipReleaseConfirmation.TryParseTime(release.GetProperty("releasedAtUtc").GetString(), out _));
        Assert.Equal(1, await WithDbAsync(db => db.LocalExecutionOwnershipReleases.CountAsync()));
        Assert.Equal(1, await WithDbAsync(db => db.Events.CountAsync(runEvent => runEvent.EventType == RunEventType.LocalExecutionOwnershipReleased)));

        // Replay: the identical request returns the same receipt with no second write, even long after the confirmation was shown.
        var replay = await client.PostAsync(Route(scene.RunId, terminal.StepId), Json(body));
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(release.GetProperty("releaseId").GetGuid(), (await BodyAsync(replay)).GetProperty("release").GetProperty("releaseId").GetGuid());
        Assert.Equal(1, await WithDbAsync(db => db.LocalExecutionOwnershipReleases.CountAsync()));

        var changed = await client.PostAsync(Route(scene.RunId, terminal.StepId), Json(ReleaseBody(candidate, sha: new string('0', 64))));
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        Assert.Equal(LocalExecutionOwnershipReleaseErrors.ConfirmationConflictCode, await ErrorCodeAsync(changed));

        var afterwards = await BodyAsync(await client.GetAsync(Route(scene.RunId, terminal.StepId)));
        Assert.Equal("Released", afterwards.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, afterwards.GetProperty("candidate").ValueKind);
    }

    [Theory]
    [InlineData("clean-exit-outcome-with-timed-out-process", "attempts", "UPDATE attempts SET \"AgentOutcome\" = 'Implemented' WHERE \"Id\" = {0}")]
    [InlineData("completed-status-with-timed-out-process", "attempts", "UPDATE attempts SET \"Status\" = 'Completed' WHERE \"Id\" = {0}")]
    [InlineData("unknown-agent-outcome", "attempts", "UPDATE attempts SET \"AgentOutcome\" = 'Unrecognized' WHERE \"Id\" = {0}")]
    [InlineData("agent-checkpoint-fingerprint", "attempts",
        "UPDATE attempts SET \"AgentCheckpointFingerprintSha256\" = '9999999999999999999999999999999999999999999999999999999999999999' WHERE \"Id\" = {0}")]
    public async Task A_terminal_result_that_the_existing_policies_could_not_have_recorded_is_unavailable_and_a_confirmed_post_is_refused_recording_nothing(
        string name, string table, string sql)
    {
        var (scene, terminal) = await TerminalAsync();
        using var client = Client();
        var candidate = (await BodyAsync(await client.GetAsync(Route(scene.RunId, terminal.StepId)))).GetProperty("candidate");
        var body = ReleaseBody(candidate);

        await WithDbAsync(async db =>
        {
            await LocalExecutionOwnershipReleaseApiSeed.TamperAsync(db, table, sql, terminal.AttemptId);
            return 0;
        });

        var read = await BodyAsync(await client.GetAsync(Route(scene.RunId, terminal.StepId)));
        Assert.Equal("Unavailable", read.GetProperty("status").GetString());
        Assert.Equal("evidence_incoherent", read.GetProperty("reasonCode").GetString());
        Assert.Equal(JsonValueKind.Null, read.GetProperty("candidate").ValueKind);
        Assert.Equal(JsonValueKind.Null, read.GetProperty("release").ValueKind);
        var refused = await client.PostAsync(Route(scene.RunId, terminal.StepId), Json(body));
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(LocalExecutionOwnershipReleaseErrors.UnavailableCode, await ErrorCodeAsync(refused));
        Assert.Equal(0, await WithDbAsync(db => db.LocalExecutionOwnershipReleases.CountAsync()));
        Assert.Equal(0, await WithDbAsync(db => db.Events.CountAsync(runEvent => runEvent.EventType == RunEventType.LocalExecutionOwnershipReleased)));
        Assert.True(name.Length > 0);
    }

    [Fact]
    public async Task A_run_that_is_not_a_manual_agent_run_is_unavailable_and_a_confirmed_post_is_refused_recording_nothing()
    {
        var (scene, terminal) = await TerminalAsync();
        using var client = Client();
        var candidate = (await BodyAsync(await client.GetAsync(Route(scene.RunId, terminal.StepId)))).GetProperty("candidate");
        var body = ReleaseBody(candidate);

        await WithDbAsync(async db =>
        {
            await LocalExecutionOwnershipReleaseApiSeed.TamperAsync(db, "runs", "UPDATE runs SET \"ExecutionMode\" = 1 WHERE \"Id\" = {0}", scene.RunId);
            return 0;
        });

        var read = await BodyAsync(await client.GetAsync(Route(scene.RunId, terminal.StepId)));
        Assert.Equal("Unavailable", read.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, read.GetProperty("candidate").ValueKind);
        var refused = await client.PostAsync(Route(scene.RunId, terminal.StepId), Json(body));
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(0, await WithDbAsync(db => db.LocalExecutionOwnershipReleases.CountAsync()));
    }

    private async Task<(int? Limit, int? Owners, string Status)> CapacityAsync(HttpClient client)
    {
        var capacity = await BodyAsync(await client.GetAsync("/api/execution-capacity"));
        int? Number(string name) => capacity.GetProperty(name).ValueKind == JsonValueKind.Number ? capacity.GetProperty(name).GetInt32() : null;
        return (Number("limit"), Number("recordedOwners"), capacity.GetProperty("status").GetString()!);
    }

    private async Task<(int Releases, int Events)> RowCountsAsync() => (
        await WithDbAsync(db => db.LocalExecutionOwnershipReleases.CountAsync()),
        await WithDbAsync(db => db.Events.CountAsync(runEvent => runEvent.EventType == RunEventType.LocalExecutionOwnershipReleased)));

    [Fact]
    public async Task A_substituted_checksum_alone_reads_unavailable_refuses_replay_and_never_reacquires_ownership()
    {
        var (scene, terminal) = await TerminalAsync();
        using var client = Client();
        var candidate = (await BodyAsync(await client.GetAsync(Route(scene.RunId, terminal.StepId)))).GetProperty("candidate");
        var body = ReleaseBody(candidate);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(Route(scene.RunId, terminal.StepId), Json(body))).StatusCode);
        Assert.Equal((1, 1), await RowCountsAsync());
        var free = await CapacityAsync(client);
        Assert.Equal("Available", free.Status);
        Assert.Equal(0, free.Owners);

        await WithDbAsync(async db =>
        {
            await LocalExecutionOwnershipReleaseApiSeed.TamperAsync(
                db, "local_execution_ownership_releases",
                "UPDATE local_execution_ownership_releases SET \"ConfirmationSha256\" = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'");
            return 0;
        });

        var read = await BodyAsync(await client.GetAsync(Route(scene.RunId, terminal.StepId)));
        Assert.Equal("Unavailable", read.GetProperty("status").GetString());
        Assert.Equal("evidence_incoherent", read.GetProperty("reasonCode").GetString());
        Assert.Equal(JsonValueKind.Null, read.GetProperty("candidate").ValueKind);
        Assert.Equal(JsonValueKind.Null, read.GetProperty("release").ValueKind);
        var replay = await client.PostAsync(Route(scene.RunId, terminal.StepId), Json(body));
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
        Assert.Equal(LocalExecutionOwnershipReleaseErrors.UnavailableCode, await ErrorCodeAsync(replay));
        Assert.DoesNotContain("hold stays", await replay.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        // No second receipt or event, and the unreadable answer reacquired nothing: capacity still admits.
        Assert.Equal((1, 1), await RowCountsAsync());
        var stillFree = await CapacityAsync(client);
        Assert.Equal("Available", stillFree.Status);
        Assert.Equal(0, stillFree.Owners);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_concrete_agent_outcome_that_changes_is_never_confirmed_by_the_old_request_before_or_after_the_release(bool released)
    {
        var (scene, terminal) = await TerminalAsync();
        using var client = Client();
        var candidate = (await BodyAsync(await client.GetAsync(Route(scene.RunId, terminal.StepId)))).GetProperty("candidate");
        var body = ReleaseBody(candidate);
        if (released)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(Route(scene.RunId, terminal.StepId), Json(body))).StatusCode);
        }

        await WithDbAsync(async db =>
        {
            await LocalExecutionOwnershipReleaseApiSeed.TamperAsync(
                db, "attempts", "UPDATE attempts SET \"AgentOutcome\" = 'CheckpointEvidenceUnavailable' WHERE \"Id\" = {0}", terminal.AttemptId);
            return 0;
        });

        var read = await BodyAsync(await client.GetAsync(Route(scene.RunId, terminal.StepId)));
        var refused = await client.PostAsync(Route(scene.RunId, terminal.StepId), Json(body));
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        if (released)
        {
            Assert.Equal("Unavailable", read.GetProperty("status").GetString());
            Assert.Equal(LocalExecutionOwnershipReleaseErrors.UnavailableCode, await ErrorCodeAsync(refused));
            Assert.Equal((1, 1), await RowCountsAsync());
            var held = await CapacityAsync(client);
            Assert.True(held.Owners is null or >= 1, $"capacity must not admit past the held owner: {held}");
        }
        else
        {
            Assert.Equal("Available", read.GetProperty("status").GetString());
            Assert.NotEqual(candidate.GetProperty("confirmationSha256").GetString(), read.GetProperty("candidate").GetProperty("confirmationSha256").GetString());
            Assert.Equal(LocalExecutionOwnershipReleaseErrors.ConfirmationStaleCode, await ErrorCodeAsync(refused));
            Assert.Equal((0, 0), await RowCountsAsync());
        }
    }

    [Fact]
    public async Task A_stale_or_expired_confirmation_an_ineligible_step_and_unavailable_evidence_are_fixed_409_refusals_that_record_nothing()
    {
        var (scene, terminal) = await TerminalAsync();
        var unavailable = await TerminalAsync(witnessed: true, observed: false);
        using var client = Client();
        var candidate = (await BodyAsync(await client.GetAsync(Route(scene.RunId, terminal.StepId)))).GetProperty("candidate");

        var stale = await client.PostAsync(Route(scene.RunId, terminal.StepId), Json(ReleaseBody(candidate, sha: new string('1', 64))));
        var expired = await client.PostAsync(
            Route(scene.RunId, terminal.StepId),
            Json(ReleaseBody(candidate, expires: LocalExecutionOwnershipReleaseConfirmation.Time(DateTimeOffset.UtcNow.AddSeconds(-5)))));
        var missing = await client.PostAsync(Route(unavailable.Scene.RunId, unavailable.Terminal.StepId), Json(ReleaseBody(candidate)));

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(LocalExecutionOwnershipReleaseErrors.ConfirmationStaleCode, await ErrorCodeAsync(stale));
        Assert.Equal(HttpStatusCode.Conflict, expired.StatusCode);
        Assert.Equal(LocalExecutionOwnershipReleaseErrors.ConfirmationStaleCode, await ErrorCodeAsync(expired));
        Assert.Equal(HttpStatusCode.Conflict, missing.StatusCode);
        Assert.Equal(LocalExecutionOwnershipReleaseErrors.UnavailableCode, await ErrorCodeAsync(missing));
        Assert.Equal(0, await WithDbAsync(db => db.LocalExecutionOwnershipReleases.CountAsync()));
        Assert.Equal(0, await WithDbAsync(db => db.Events.CountAsync(runEvent => runEvent.EventType == RunEventType.LocalExecutionOwnershipReleased)));
        var notFound = await client.PostAsync(Route(Guid.NewGuid(), terminal.StepId), Json(ReleaseBody(candidate)));
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
    }

    // ---- the strict body ------------------------------------------------------------------------------------------------------------

    private static readonly string Observation = "11111111-1111-1111-1111-111111111111";
    private static readonly string Sha = new('a', 64);
    private const string Expiry = "2030-01-01T00:00:00.0000000Z";

    public static TheoryData<string> MalformedBodies =>
    [
        "{}",
        "[]",
        "null",
        "\"text\"",
        "",
        "{\"observationId\":\"" + "11111111-1111-1111-1111-111111111111" + "\",\"confirmationVersion\":\"1\",\"confirmationSha256\":\"" + "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" + "\"}",
        "{\"observationId\":\"11111111-1111-1111-1111-111111111111\",\"confirmationVersion\":\"1\",\"confirmationSha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"confirmationExpiresAtUtc\":\"2030-01-01T00:00:00.0000000Z\",\"force\":\"true\"}",
        "{\"observationId\":\"11111111-1111-1111-1111-111111111111\",\"confirmationVersion\":\"1\",\"confirmationVersion\":\"1\",\"confirmationSha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"confirmationExpiresAtUtc\":\"2030-01-01T00:00:00.0000000Z\"}",
        "{\"ObservationId\":\"11111111-1111-1111-1111-111111111111\",\"confirmationVersion\":\"1\",\"confirmationSha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"confirmationExpiresAtUtc\":\"2030-01-01T00:00:00.0000000Z\"}",
        "{\"observation\\u0049d\":\"11111111-1111-1111-1111-111111111111\",\"confirmationVersion\":\"1\",\"confirmationSha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"confirmationExpiresAtUtc\":\"2030-01-01T00:00:00.0000000Z\"}",
        "{\"observationId\":\"11111111-1111-1111-1111-111111111111\",\"confirmationVersion\":1,\"confirmationSha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"confirmationExpiresAtUtc\":\"2030-01-01T00:00:00.0000000Z\"}",
        "{\"observationId\":\"11111111-1111-1111-1111-111111111111\",\"confirmationVersion\":\"01\",\"confirmationSha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"confirmationExpiresAtUtc\":\"2030-01-01T00:00:00.0000000Z\"}",
        "{\"observationId\":\"11111111-1111-1111-1111-111111111111\",\"confirmationVersion\":\"0\",\"confirmationSha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"confirmationExpiresAtUtc\":\"2030-01-01T00:00:00.0000000Z\"}",
        "{\"observationId\":\"00000000-0000-0000-0000-000000000000\",\"confirmationVersion\":\"1\",\"confirmationSha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"confirmationExpiresAtUtc\":\"2030-01-01T00:00:00.0000000Z\"}",
        "{\"observationId\":\" 11111111-1111-1111-1111-111111111111\",\"confirmationVersion\":\"1\",\"confirmationSha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"confirmationExpiresAtUtc\":\"2030-01-01T00:00:00.0000000Z\"}",
        "{\"observationId\":\"{11111111-1111-1111-1111-111111111111}\",\"confirmationVersion\":\"1\",\"confirmationSha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"confirmationExpiresAtUtc\":\"2030-01-01T00:00:00.0000000Z\"}",
        "{\"observationId\":\"11111111-1111-1111-1111-111111111111\",\"confirmationVersion\":\"1\",\"confirmationSha256\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\",\"confirmationExpiresAtUtc\":\"2030-01-01T00:00:00.0000000Z\"}",
        "{\"observationId\":\"11111111-1111-1111-1111-111111111111\",\"confirmationVersion\":\"1\",\"confirmationSha256\":\"aaa\",\"confirmationExpiresAtUtc\":\"2030-01-01T00:00:00.0000000Z\"}",
        "{\"observationId\":\"11111111-1111-1111-1111-111111111111\",\"confirmationVersion\":\"1\",\"confirmationSha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"confirmationExpiresAtUtc\":\"2030-01-01T00:00:00Z\"}",
        "{\"observationId\":\"11111111-1111-1111-1111-111111111111\",\"confirmationVersion\":\"1\",\"confirmationSha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"confirmationExpiresAtUtc\":\"2030-01-01T00:00:00.0000000+01:00\"}",
        "{\"observationId\":\"11111111-1111-1111-1111-111111111111\",\"confirmationVersion\":\"1\",\"confirmationSha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"confirmationExpiresAtUtc\":null}",
        "{\"observationId\":\"11111111-1111-1111-1111-111111111111\",\"confirmationVersion\":\"1\",\"confirmationSha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"confirmationExpiresAtUtc\":\"2030-01-01T00:00:00.0000000Z\"} trailing",
        "{\"observationId\":\"11111111-1111-1111-1111-111111111111\",\"confirmationVersion\":\"1\",\"confirmationSha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"confirmationExpiresAtUtc\":\"2030-01-01T00:00:00.0000000Z\",}",
    ];

    private static string Valid(string? observation = null) => JsonSerializer.Serialize(new
    {
        observationId = observation ?? Observation,
        confirmationVersion = "1",
        confirmationSha256 = Sha,
        confirmationExpiresAtUtc = Expiry,
    });

    [Theory]
    [MemberData(nameof(MalformedBodies))]
    public async Task A_malformed_or_non_exact_body_is_refused_before_anything_is_dispatched(string body)
    {
        var (scene, terminal) = await TerminalAsync();
        using var client = Client();

        var response = await client.PostAsync(Route(scene.RunId, terminal.StepId), Json(body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(LocalExecutionOwnershipReleaseErrors.InvalidRequestCode, await ErrorCodeAsync(response));
        Assert.DoesNotContain(Observation, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(0, await WithDbAsync(db => db.LocalExecutionOwnershipReleases.CountAsync()));
    }

    [Fact]
    public async Task A_query_key_and_a_non_json_content_type_are_refused_and_an_oversized_body_is_a_fixed_413()
    {
        var (scene, terminal) = await TerminalAsync();
        using var client = Client();
        var valid = Valid();

        var query = await client.PostAsync($"{Route(scene.RunId, terminal.StepId)}?force=true", Json(valid));
        var text = await client.PostAsync(Route(scene.RunId, terminal.StepId), new StringContent(valid, Encoding.UTF8, "text/plain"));
        var oversized = await client.PostAsync(Route(scene.RunId, terminal.StepId), Json(valid + new string(' ', 513 - valid.Length)));
        var wayOver = await client.PostAsync(Route(scene.RunId, terminal.StepId), Json(valid + new string(' ', 4096)));

        Assert.Equal(HttpStatusCode.BadRequest, query.StatusCode);
        Assert.Equal(LocalExecutionOwnershipReleaseErrors.InvalidRequestCode, await ErrorCodeAsync(query));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, text.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
        Assert.Equal(LocalExecutionOwnershipReleaseErrors.BodyTooLargeCode, await ErrorCodeAsync(oversized));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, wayOver.StatusCode);
        Assert.Equal(0, await WithDbAsync(db => db.LocalExecutionOwnershipReleases.CountAsync()));
    }

    [Fact]
    public async Task A_valid_body_of_exactly_512_bytes_is_within_the_bound_and_reaches_the_use_case()
    {
        var (scene, terminal) = await TerminalAsync();
        using var client = Client();
        var valid = Valid();
        Assert.True(valid.Length < 512);

        var response = await client.PostAsync(Route(scene.RunId, terminal.StepId), Json(valid + new string(' ', 512 - valid.Length)));

        // Within the transport bound: the body is shaped correctly, so the use case (not the filter) refuses its stale confirmation.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(LocalExecutionOwnershipReleaseErrors.ConfirmationStaleCode, await ErrorCodeAsync(response));
    }
}
