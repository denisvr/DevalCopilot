using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Application.Features.Runs.Policies.AgentSupervisions;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.AgentSupervisions;

/// <summary>
/// ADR-0056's one protected MVC operation through the real host: authentication and the MVC authorization inventory, the bodyless and
/// queryless contract, the non-disclosing 404, the closed status vocabulary with its nullability relationships, the closed response
/// graph (exact property sets, no extra field), canonical UTC strings and decimal-string Int64 values, effective versus recorded
/// attention, and that a read writes nothing. Nothing here runs a provider, a process or a supervisor.
/// </summary>
public sealed partial class AgentSupervisionEndpointTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    private static readonly string Fingerprint = new('a', 64);
    private static readonly Regex CanonicalUtc = new(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{7}Z$", RegexOptions.CultureInvariant);

    private static readonly string[] TopProperties =
        ["runId", "status", "reasonCode", "reasonText", "nextActionCode", "nextActionText", "attempt", "observation", "attention", "observedAtUtc"];

    private static readonly string[] AttemptProperties =
    [
        "attemptId", "attemptNumber", "role", "provider", "responseContract", "status", "outcome", "dispatched", "claimedAtUtc",
        "dispatchedAtUtc", "completedAtUtc",
    ];

    private static readonly string[] ObservationProperties =
    [
        "hostInstanceId", "observerId", "ownedByCurrentHost", "revision", "phase", "startedAtUtc", "lastHeartbeatAtUtc",
        "phaseEnteredAtUtc", "phaseDeadlineUtc", "open", "closureReason", "closedAtUtc",
    ];

    private static readonly string[] AttentionProperties =
        ["effectiveReasonCode", "effectiveReasonText", "effectiveAtUtc", "recordedReasonCode", "recordedAtUtc"];

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

    private AgentSupervisionHostInstance Host => factory.Services.GetRequiredService<AgentSupervisionHostInstance>();

    private sealed record Seeded(Guid RunId, Guid AttemptId, Guid? ReceiptId, string WorkspacePath);

    /// <summary>One ManualAgent run, optionally with one claimed Agent attempt and optionally its observation, registered by this very
    /// host at <paramref name="startedAt"/>.</summary>
    private Task<Seeded> SeedAsync(
        bool attempt = true, bool observe = true, DateTimeOffset? startedAt = null, AgentResponseContract contract = AgentResponseContract.Proposal) =>
        WithDbAsync(async db =>
        {
            var now = DateTimeOffset.UtcNow.AddMinutes(-30);
            var project = Project.Register(Guid.NewGuid(), "Supervision wire", $@"C:\repos\{Guid.NewGuid():N}", now);
            var run = Run.RecordClassifiedIntent(
                Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), RunExecutionMode.ManualAgent, "Observe", now);
            var workspacePath = $@"C:\workspaces\sentinel-{Guid.NewGuid():N}";
            var workspace = GitWorkspace.Prepare(Guid.NewGuid(), project.Id, 1, workspacePath, "branch", new string('a', 40), "main", now);
            workspace.MarkReady();
            var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, workspace.ReserveCheckpointNumber(), now, new string('a', 40), Fingerprint, []);
            db.Projects.Add(project);
            db.Runs.Add(run);
            db.GitWorkspaces.Add(workspace);
            db.GitCheckpoints.Add(checkpoint);
            Guid attemptId = Guid.Empty;
            Guid? receiptId = null;
            if (attempt)
            {
                var claimed = contract switch
                {
                    AgentResponseContract.CriticalReview => Attempt.ClaimAgentCriticalReview(
                        Guid.NewGuid(), run.Id, 1, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(10), 1024, 2048, now, 1),
                    _ => Attempt.ClaimAgent(
                        Guid.NewGuid(), run.Id, 1, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(10), 1024, 2048, now, 1),
                };
                attemptId = claimed.Id;
                db.Attempts.Add(claimed);
                await db.SaveChangesAsync();
                if (observe)
                {
                    var receipt = AgentSupervision.Register(
                        Guid.NewGuid(), run.Id, claimed.Id, Host.Id, Guid.NewGuid(), startedAt ?? DateTimeOffset.UtcNow);
                    receiptId = receipt.Id;
                    db.AgentSupervisions.Add(receipt);
                }
            }

            await db.SaveChangesAsync();
            return new Seeded(run.Id, attemptId, receiptId, workspacePath);
        });

    private static string Route(Guid runId) => $"/api/runs/{runId}/agent-supervision";

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static void AssertExactProperties(JsonElement element, string[] expected) =>
        Assert.Equal(expected.Order(StringComparer.Ordinal), element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));

    private static void AssertGraphIsClosed(JsonElement body)
    {
        AssertExactProperties(body, TopProperties);
        if (body.GetProperty("attempt") is { ValueKind: JsonValueKind.Object } attempt)
        {
            AssertExactProperties(attempt, AttemptProperties);
        }

        if (body.GetProperty("observation") is { ValueKind: JsonValueKind.Object } observation)
        {
            AssertExactProperties(observation, ObservationProperties);
        }

        if (body.GetProperty("attention") is { ValueKind: JsonValueKind.Object } attention)
        {
            AssertExactProperties(attention, AttentionProperties);
        }
    }

    private async Task<JsonElement> GetOkAsync(Guid runId)
    {
        using var client = Client();
        var response = await client.GetAsync(Route(runId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyAsync(response);
        AssertGraphIsClosed(body);
        Assert.Matches(CanonicalUtc, body.GetProperty("observedAtUtc").GetString()!);
        return body;
    }

    // ---- authentication and the MVC authorization inventory ------------------------------------------------------------------

    [Fact]
    public async Task The_operation_requires_the_launch_session_secret()
    {
        var seed = await SeedAsync();
        using var anonymous = factory.CreateClient();
        using var wrong = factory.CreateClient();
        wrong.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-secret");

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Route(seed.RunId))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await wrong.GetAsync(Route(seed.RunId))).StatusCode);
    }

    [Fact]
    public void Exactly_one_agent_supervision_route_exists_a_GET_that_is_authorized_and_not_anonymous()
    {
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.Contains("agent-supervision", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        var endpoint = Assert.Single(endpoints);
        Assert.Equal("api/runs/{runId:guid}/agent-supervision", endpoint.RoutePattern.RawText);
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

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), Route(seed.RunId)));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal(1, await WithDbAsync(db => db.AgentSupervisions.CountAsync(receipt => receipt.RunId == seed.RunId)));
    }

    // ---- the bodyless, queryless and non-disclosing contract -----------------------------------------------------------------

    [Fact]
    public async Task An_unknown_run_is_a_fixed_not_found_that_echoes_nothing()
    {
        var unknown = Guid.NewGuid();
        using var client = Client();

        var response = await client.GetAsync(Route(unknown));
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("agent_supervision.not_found", text, StringComparison.Ordinal);
        // The shared Problem Details contract names the request path in its own instance member; nothing else may carry the identifier.
        using var problem = JsonDocument.Parse(text);
        foreach (var property in problem.RootElement.EnumerateObject().Where(property => property.Name != "instance"))
        {
            Assert.DoesNotContain(unknown.ToString(), property.Value.GetRawText(), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData("?a=1")]
    [InlineData("?runId=1")]
    [InlineData("?includeAttention=true")]
    public async Task A_query_string_is_refused_with_the_fixed_non_echoing_error(string query)
    {
        var seed = await SeedAsync();
        using var client = Client();

        var response = await client.GetAsync(Route(seed.RunId) + query);
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("agent_supervision.query_not_allowed", text, StringComparison.Ordinal);
        Assert.DoesNotContain(query.TrimStart('?'), text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_body_of_any_kind_is_refused_before_dispatch()
    {
        var seed = await SeedAsync();
        using var client = Client();

        var declared = new HttpRequestMessage(HttpMethod.Get, Route(seed.RunId)) { Content = new StringContent("{\"secret\":\"echo-me\"}") };
        var declaredResponse = await client.SendAsync(declared);
        var chunked = new HttpRequestMessage(HttpMethod.Get, Route(seed.RunId)) { Content = new StringContent(string.Empty) };
        chunked.Headers.TransferEncodingChunked = true;
        var chunkedResponse = await client.SendAsync(chunked);

        foreach (var response in new[] { declaredResponse, chunkedResponse })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var text = await response.Content.ReadAsStringAsync();
            Assert.Contains("agent_supervision.body_not_allowed", text, StringComparison.Ordinal);
            Assert.DoesNotContain("echo-me", text, StringComparison.Ordinal);
        }
    }

    // ---- the closed status vocabulary and its nullability relationships ------------------------------------------------------

    [Fact]
    public async Task A_run_without_an_agent_attempt_has_only_its_status_and_the_host_time()
    {
        var seed = await SeedAsync(attempt: false);

        var body = await GetOkAsync(seed.RunId);

        Assert.Equal("NoAgentAttempt", body.GetProperty("status").GetString());
        Assert.Equal(seed.RunId, body.GetProperty("runId").GetGuid());
        foreach (var name in new[] { "reasonCode", "reasonText", "attempt", "observation", "attention" })
        {
            Assert.Equal(JsonValueKind.Null, body.GetProperty(name).ValueKind);
        }

        Assert.Equal("no_agent_attempt_recorded", body.GetProperty("nextActionCode").GetString());
    }

    [Fact]
    public async Task A_missing_receipt_is_NotObserved_with_the_recorded_attempt_and_no_heartbeat()
    {
        var seed = await SeedAsync(observe: false);

        var body = await GetOkAsync(seed.RunId);

        Assert.Equal("NotObserved", body.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("observation").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("attention").ValueKind);
        var attempt = body.GetProperty("attempt");
        Assert.Equal(seed.AttemptId, attempt.GetProperty("attemptId").GetGuid());
        Assert.Equal(1, attempt.GetProperty("attemptNumber").GetInt32());
        Assert.Equal(("Planner", "Codex", "Proposal", "Running"), (
            attempt.GetProperty("role").GetString()!, attempt.GetProperty("provider").GetString()!,
            attempt.GetProperty("responseContract").GetString()!, attempt.GetProperty("status").GetString()!));
        Assert.False(attempt.GetProperty("dispatched").GetBoolean());
        Assert.Equal(JsonValueKind.Null, attempt.GetProperty("dispatchedAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, attempt.GetProperty("outcome").ValueKind);
        Assert.Matches(CanonicalUtc, attempt.GetProperty("claimedAtUtc").GetString()!);
        Assert.Equal("inspect_the_recorded_attempt", body.GetProperty("nextActionCode").GetString());
    }

    [Fact]
    public async Task A_fresh_observation_is_Observed_with_canonical_times_a_decimal_revision_and_no_attention()
    {
        var seed = await SeedAsync(contract: AgentResponseContract.CriticalReview);

        var body = await GetOkAsync(seed.RunId);

        Assert.Equal("Observed", body.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("attention").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("reasonCode").ValueKind);
        Assert.Equal(("CriticalReviewer", "ClaudeCode", "CriticalReview"), (
            body.GetProperty("attempt").GetProperty("role").GetString()!, body.GetProperty("attempt").GetProperty("provider").GetString()!,
            body.GetProperty("attempt").GetProperty("responseContract").GetString()!));
        var observation = body.GetProperty("observation");
        Assert.Equal("Preparing", observation.GetProperty("phase").GetString());
        Assert.Equal(JsonValueKind.String, observation.GetProperty("revision").ValueKind);
        Assert.Equal("1", observation.GetProperty("revision").GetString());
        Assert.True(observation.GetProperty("open").GetBoolean());
        Assert.True(observation.GetProperty("ownedByCurrentHost").GetBoolean());
        Assert.Equal(Host.Id, observation.GetProperty("hostInstanceId").GetGuid());
        Assert.Equal(JsonValueKind.Null, observation.GetProperty("closureReason").ValueKind);
        Assert.Equal(JsonValueKind.Null, observation.GetProperty("closedAtUtc").ValueKind);
        foreach (var name in new[] { "startedAtUtc", "lastHeartbeatAtUtc", "phaseEnteredAtUtc", "phaseDeadlineUtc" })
        {
            Assert.Matches(CanonicalUtc, observation.GetProperty(name).GetString()!);
        }

        Assert.Equal("wait_and_refresh", body.GetProperty("nextActionCode").GetString());
    }

    [Fact]
    public async Task An_elapsed_deadline_is_NeedsAttention_before_it_is_persisted_and_the_recorded_fields_are_absent()
    {
        var seed = await SeedAsync(startedAt: DateTimeOffset.UtcNow.AddMinutes(-10));

        var body = await GetOkAsync(seed.RunId);

        Assert.Equal("NeedsAttention", body.GetProperty("status").GetString());
        Assert.Equal("stale_heartbeat", body.GetProperty("reasonCode").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("reasonText").GetString()));
        var attention = body.GetProperty("attention");
        Assert.Equal("stale_heartbeat", attention.GetProperty("effectiveReasonCode").GetString());
        Assert.Matches(CanonicalUtc, attention.GetProperty("effectiveAtUtc").GetString()!);
        Assert.Equal(JsonValueKind.Null, attention.GetProperty("recordedReasonCode").ValueKind);
        Assert.Equal(JsonValueKind.Null, attention.GetProperty("recordedAtUtc").ValueKind);
        Assert.Equal("inspect_and_reconcile_before_further_work", body.GetProperty("nextActionCode").GetString());
        Assert.Equal(1, await WithDbAsync(db => db.AgentSupervisions.Where(receipt => receipt.RunId == seed.RunId).Select(receipt => receipt.Revision).SingleAsync()));
    }

    [Fact]
    public async Task A_recorded_attention_on_a_closed_observation_is_NeedsAttention_with_its_closure()
    {
        var seed = await SeedAsync(startedAt: DateTimeOffset.UtcNow.AddMinutes(-10));
        await WithDbAsync(async db =>
        {
            var receipt = await db.AgentSupervisions.SingleAsync(candidate => candidate.RunId == seed.RunId);
            receipt.RecordAttention(AgentSupervisionAttentionReason.ObserverEndedWithoutTerminalResult, receipt.StartedAtUtc.AddSeconds(1));
            receipt.Close(AgentSupervisionClosureReason.ObserverEnded, receipt.StartedAtUtc.AddSeconds(1));
            await db.SaveChangesAsync();
            return 0;
        });

        var body = await GetOkAsync(seed.RunId);

        Assert.Equal("NeedsAttention", body.GetProperty("status").GetString());
        Assert.Equal("observer_ended_without_terminal_result", body.GetProperty("reasonCode").GetString());
        var observation = body.GetProperty("observation");
        Assert.False(observation.GetProperty("open").GetBoolean());
        Assert.Equal("observer_ended", observation.GetProperty("closureReason").GetString());
        Assert.Matches(CanonicalUtc, observation.GetProperty("closedAtUtc").GetString()!);
        var attention = body.GetProperty("attention");
        Assert.Equal("observer_ended_without_terminal_result", attention.GetProperty("recordedReasonCode").GetString());
        Assert.Matches(CanonicalUtc, attention.GetProperty("recordedAtUtc").GetString()!);
    }

    [Fact]
    public async Task A_terminal_attempt_is_Ended_with_its_real_status_and_the_attention_history_kept()
    {
        // Observed long enough ago that the recorded phase deadline (and so the recorded attention) lies before the host clock.
        var seed = await SeedAsync(startedAt: DateTimeOffset.UtcNow.AddMinutes(-10));
        await WithDbAsync(async db =>
        {
            var attempt = await db.Attempts.SingleAsync(candidate => candidate.Id == seed.AttemptId);
            attempt.Fail(DateTimeOffset.UtcNow);
            var receipt = await db.AgentSupervisions.SingleAsync(candidate => candidate.RunId == seed.RunId);
            receipt.RecordAttention(AgentSupervisionAttentionReason.PhaseDeadlineElapsed, receipt.PhaseDeadlineUtc);
            await db.SaveChangesAsync();
            return 0;
        });

        var body = await GetOkAsync(seed.RunId);

        Assert.Equal("Ended", body.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("reasonCode").ValueKind);
        Assert.Equal("Failed", body.GetProperty("attempt").GetProperty("status").GetString());
        Assert.Matches(CanonicalUtc, body.GetProperty("attempt").GetProperty("completedAtUtc").GetString()!);
        Assert.Equal("phase_deadline_elapsed", body.GetProperty("attention").GetProperty("recordedReasonCode").GetString());
        Assert.Equal("review_the_recorded_result", body.GetProperty("nextActionCode").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_recorded_attention_or_completion_later_than_the_host_clock_is_served_as_Unavailable_never_clamped(bool terminal)
    {
        // ASUP-R3 through the real endpoint and serializer: the recorded first attention lies two minutes ahead of the host clock
        // (and, for a terminal attempt, so does its completion). Neither NeedsAttention nor Ended is served.
        var seed = await SeedAsync();
        await WithDbAsync(async db =>
        {
            var receipt = await db.AgentSupervisions.SingleAsync(candidate => candidate.RunId == seed.RunId);
            receipt.RecordAttention(AgentSupervisionAttentionReason.PhaseDeadlineElapsed, receipt.PhaseDeadlineUtc);
            if (terminal)
            {
                var attempt = await db.Attempts.SingleAsync(candidate => candidate.Id == seed.AttemptId);
                attempt.Fail(DateTimeOffset.UtcNow.AddMinutes(2));
            }

            await db.SaveChangesAsync();
            return 0;
        });

        var body = await GetOkAsync(seed.RunId);

        Assert.Equal("Unavailable", body.GetProperty("status").GetString());
        Assert.Equal("chronology_invalid", body.GetProperty("reasonCode").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("attention").ValueKind);
        Assert.Equal("inspect_and_reconcile_before_further_work", body.GetProperty("nextActionCode").GetString());
    }

    [Fact]
    public async Task Facts_that_cannot_be_trusted_are_Unavailable_with_a_fixed_reason_and_no_fresh_claim()
    {
        var seed = await SeedAsync(startedAt: DateTimeOffset.UtcNow.AddMinutes(5));

        var body = await GetOkAsync(seed.RunId);

        Assert.Equal("Unavailable", body.GetProperty("status").GetString());
        Assert.Equal("chronology_invalid", body.GetProperty("reasonCode").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("attention").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("observation").ValueKind);
    }

    [Fact]
    public async Task The_revision_roundtrips_exactly_as_a_decimal_string_at_the_signed_64_bit_maximum()
    {
        var seed = await SeedAsync();
        await WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync("UPDATE agent_supervisions SET Revision = 9223372036854775807 WHERE RunId = {0}", seed.RunId);
            return 0;
        });
        using var client = Client();

        var text = await (await client.GetAsync(Route(seed.RunId))).Content.ReadAsStringAsync();

        Assert.Contains("\"revision\":\"9223372036854775807\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_response_never_discloses_a_path_a_secret_or_another_runs_facts_and_a_read_writes_nothing()
    {
        var seed = await SeedAsync();
        var other = await SeedAsync();
        var revisionBefore = await WithDbAsync(db => db.AgentSupervisions.Where(receipt => receipt.RunId == seed.RunId).Select(receipt => receipt.Revision).SingleAsync());
        var eventsBefore = await WithDbAsync(db => db.Events.CountAsync());
        using var client = Client();

        var text = await (await client.GetAsync(Route(seed.RunId))).Content.ReadAsStringAsync();
        await client.GetAsync(Route(seed.RunId));

        Assert.DoesNotContain(seed.WorkspacePath, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(other.RunId.ToString(), text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(other.AttemptId.ToString(), text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ApiWebApplicationFactory.ValidSecret, text, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\", text, StringComparison.Ordinal);
        Assert.DoesNotContain(Fingerprint, text, StringComparison.Ordinal);
        Assert.Equal(revisionBefore, await WithDbAsync(db => db.AgentSupervisions.Where(receipt => receipt.RunId == seed.RunId).Select(receipt => receipt.Revision).SingleAsync()));
        Assert.Equal(eventsBefore, await WithDbAsync(db => db.Events.CountAsync()));
    }

    [Fact]
    public async Task A_read_of_one_run_never_selects_another_runs_attempt()
    {
        var seed = await SeedAsync();
        var emptyRun = await SeedAsync(attempt: false);

        var body = await GetOkAsync(emptyRun.RunId);

        Assert.Equal("NoAgentAttempt", body.GetProperty("status").GetString());
        Assert.Equal(seed.RunId, (await GetOkAsync(seed.RunId)).GetProperty("runId").GetGuid());
    }
}
