using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionSequences;

/// <summary>ADR-0062's two protected queue operations through the real host: authentication, the strict queue availability query and
/// the strict five-member queue body, the separate queue confirmation (neither mode's confirmation works for the other), the recorded
/// queued receipt with its pending admission disclosure, the replay and cross-mode conflict contract, and the fact that queue intake
/// observes no Git and starts nothing.</summary>
public sealed class LocalExecutionQueueEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;
    private readonly LocalExecutionSequenceMatchingEvidenceReader evidence = new();

    public LocalExecutionQueueEndpointTests(ApiWebApplicationFactory baseFactory)
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

    private Task<LocalExecutionSequenceApiScene> SeedAsync() =>
        WithDbAsync(db => LocalExecutionSequenceApiSeed.SeedAsync(db, 2, RunExecutionMode.ManualAgent, true, null));

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static string ImmediateRoute(Guid runId) => $"/api/runs/{runId}/local-execution-sequence";

    private static string QueueRoute(Guid runId) => $"{ImmediateRoute(runId)}/queue";

    private static string QueueAvailabilityRoute(Guid runId, string plan) => $"{QueueRoute(runId)}/availability?planProposalMessageId={plan}";

    private static string ImmediateAvailabilityRoute(Guid runId, string plan) => $"{ImmediateRoute(runId)}/availability/{plan}";

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

    private static string Body(LocalExecutionSequenceApiScene scene, string confirmation) =>
        JsonSerializer.Serialize(new
        {
            planProposalMessageId = scene.PlanId,
            workspaceId = scene.WorkspaceId,
            checkpointId = scene.CheckpointId,
            fingerprintSha256 = LocalExecutionSequenceApiSeed.Fingerprint,
            confirmationSha256 = confirmation,
        });

    private async Task<string> QueueConfirmationAsync(LocalExecutionSequenceApiScene scene)
    {
        using var client = Client();
        var response = await client.GetAsync(QueueAvailabilityRoute(scene.RunId, scene.PlanId.ToString()));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await BodyAsync(response)).GetProperty("confirmationSha256").GetString()!;
    }

    private async Task<string> ImmediateConfirmationAsync(LocalExecutionSequenceApiScene scene)
    {
        using var client = Client();
        var response = await client.GetAsync(ImmediateAvailabilityRoute(scene.RunId, scene.PlanId.ToString()));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await BodyAsync(response)).GetProperty("confirmationSha256").GetString()!;
    }

    private Task<List<LocalExecutionSequence>> SequencesAsync(Guid runId) =>
        WithDbAsync(db => db.LocalExecutionSequences.AsNoTracking().Where(sequence => sequence.RunId == runId).ToListAsync());

    [Fact]
    public void The_two_queue_routes_exist_each_authorized_with_its_one_method_and_none_anonymous()
    {
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.Contains("local-execution-sequence/queue", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        Assert.Equal(2, endpoints.Count);
        Assert.Equal(
            new HashSet<string>
            {
                "POST api/runs/{runId:guid}/local-execution-sequence/queue",
                "GET api/runs/{runId:guid}/local-execution-sequence/queue/availability",
            },
            endpoints.Select(
                endpoint => $"{endpoint.Metadata.GetOrderedMetadata<HttpMethodMetadata>().Single().HttpMethods.Single()} {endpoint.RoutePattern.RawText}")
                .ToHashSet());
        Assert.All(endpoints, endpoint =>
        {
            Assert.NotEmpty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
            Assert.Empty(endpoint.Metadata.GetOrderedMetadata<IAllowAnonymous>());
        });
    }

    [Fact]
    public async Task Both_queue_operations_require_authentication_and_an_unauthenticated_post_changes_nothing()
    {
        var scene = await SeedAsync();
        using var anonymous = factory.CreateClient();

        var queue = await anonymous.PostAsync(QueueRoute(scene.RunId), Json(Body(scene, new string('c', 64))));
        var availability = await anonymous.GetAsync(QueueAvailabilityRoute(scene.RunId, scene.PlanId.ToString()));

        Assert.Equal(HttpStatusCode.Unauthorized, queue.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, availability.StatusCode);
        Assert.Empty(await SequencesAsync(scene.RunId));
    }

    [Fact]
    public async Task The_queue_confirmation_is_separate_and_neither_mode_confirmation_works_for_the_other_mode()
    {
        var scene = await SeedAsync();
        var queueConfirmation = await QueueConfirmationAsync(scene);
        var immediateConfirmation = await ImmediateConfirmationAsync(scene);
        using var client = Client();

        Assert.NotEqual(queueConfirmation, immediateConfirmation);
        var queueWithImmediate = await client.PostAsync(QueueRoute(scene.RunId), Json(Body(scene, immediateConfirmation)));
        var immediateWithQueue = await client.PostAsync(ImmediateRoute(scene.RunId), Json(Body(scene, queueConfirmation)));

        Assert.Equal(HttpStatusCode.Conflict, queueWithImmediate.StatusCode);
        Assert.Equal(LocalExecutionSequenceErrors.ConfirmationStaleCode, await ErrorCodeAsync(queueWithImmediate));
        Assert.Equal(HttpStatusCode.Conflict, immediateWithQueue.StatusCode);
        Assert.Equal(LocalExecutionSequenceErrors.ConfirmationStaleCode, await ErrorCodeAsync(immediateWithQueue));
        Assert.Empty(await SequencesAsync(scene.RunId));
    }

    [Fact]
    public async Task Queueing_records_the_waiting_cycle_with_one_pending_admission_and_observes_no_git_and_starts_nothing()
    {
        var scene = await SeedAsync();
        var confirmation = await QueueConfirmationAsync(scene);
        var capturesBefore = evidence.Captures;
        var attemptsBefore = await WithDbAsync(db => db.Attempts.AsNoTracking().CountAsync(attempt => attempt.RunId == scene.RunId));
        using var client = Client();

        var response = await client.PostAsync(QueueRoute(scene.RunId), Json(Body(scene, confirmation)));
        var body = await BodyAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("WaitForAdmission", body.GetProperty("admissionMode").GetString());
        Assert.False(body.GetProperty("replayed").GetBoolean());
        Assert.Equal("WaitingForAdmission", body.GetProperty("state").GetString());
        var queue = body.GetProperty("queue");
        Assert.Equal("Waiting", queue.GetProperty("admissionState").GetString());
        Assert.Equal(JsonValueKind.String, queue.GetProperty("admissionNumber").ValueKind);
        Assert.Equal(capturesBefore, evidence.Captures);
        var sequence = Assert.Single(await SequencesAsync(scene.RunId));
        Assert.Equal(LocalExecutionAdmissionMode.WaitForAdmission, sequence.AdmissionMode);
        var counts = await WithDbAsync(async db => new
        {
            Admissions = await db.LocalExecutionAdmissions.AsNoTracking().CountAsync(admission => admission.SequenceId == sequence.Id),
            Steps = await db.LocalExecutionSequenceSteps.AsNoTracking().CountAsync(step => step.SequenceId == sequence.Id),
            Attempts = await db.Attempts.AsNoTracking().CountAsync(attempt => attempt.RunId == scene.RunId),
        });
        Assert.Equal(1, counts.Admissions);
        Assert.Equal(0, counts.Steps);
        Assert.Equal(attemptsBefore, counts.Attempts);
    }

    [Fact]
    public async Task An_identical_queue_replay_returns_the_recorded_receipt_and_the_other_mode_or_tuple_conflicts()
    {
        var scene = await SeedAsync();
        var confirmation = await QueueConfirmationAsync(scene);
        var immediateConfirmation = await ImmediateConfirmationAsync(scene);
        using var client = Client();

        var first = await BodyAsync(await client.PostAsync(QueueRoute(scene.RunId), Json(Body(scene, confirmation))));
        var replay = await client.PostAsync(QueueRoute(scene.RunId), Json(Body(scene, confirmation)));
        var replayBody = await BodyAsync(replay);
        var immediate = await client.PostAsync(ImmediateRoute(scene.RunId), Json(Body(scene, immediateConfirmation)));

        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.True(replayBody.GetProperty("replayed").GetBoolean());
        Assert.Equal(first.GetProperty("sequenceId").GetGuid(), replayBody.GetProperty("sequenceId").GetGuid());
        Assert.Equal(first.GetProperty("queue").GetProperty("admissionId").GetGuid(), replayBody.GetProperty("queue").GetProperty("admissionId").GetGuid());
        Assert.Equal(HttpStatusCode.Conflict, immediate.StatusCode);
        Assert.Single(await SequencesAsync(scene.RunId));
        Assert.Equal(1, await WithDbAsync(db => db.LocalExecutionAdmissions.AsNoTracking().CountAsync()));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("\"text\"")]
    [InlineData("")]
    [InlineData("{}")]
    public async Task A_malformed_or_non_exact_queue_body_is_refused_before_anything_is_dispatched(string body)
    {
        var scene = await SeedAsync();
        using var client = Client();

        var response = await client.PostAsync(QueueRoute(scene.RunId), Json(body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(LocalExecutionSequenceErrors.InvalidRequestCode, await ErrorCodeAsync(response));
        Assert.Empty(await SequencesAsync(scene.RunId));
    }

    [Fact]
    public async Task An_extra_member_a_query_key_and_an_oversized_body_are_refused_and_record_nothing()
    {
        var scene = await SeedAsync();
        var confirmation = await QueueConfirmationAsync(scene);
        using var client = Client();
        var valid = Body(scene, confirmation);

        var extra = await client.PostAsync(QueueRoute(scene.RunId), Json(valid.TrimEnd('}') + ",\"priority\":1}"));
        var query = await client.PostAsync(QueueRoute(scene.RunId) + "?mode=queue", Json(valid));
        var oversized = await client.PostAsync(QueueRoute(scene.RunId), Json(valid.TrimEnd('}') + ",\"x\":\"" + new string('a', 2048) + "\"}"));

        Assert.Equal(HttpStatusCode.BadRequest, extra.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, query.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
        Assert.Empty(await SequencesAsync(scene.RunId));
    }

    [Fact]
    public async Task The_queue_availability_is_strict_about_its_one_query_key_and_a_missing_run_is_the_fixed_404()
    {
        var scene = await SeedAsync();
        using var client = Client();

        var missingKey = await client.GetAsync($"{QueueRoute(scene.RunId)}/availability");
        var duplicate = await client.GetAsync($"{QueueRoute(scene.RunId)}/availability?planProposalMessageId={scene.PlanId}&planProposalMessageId={scene.PlanId}");
        var extra = await client.GetAsync(QueueAvailabilityRoute(scene.RunId, scene.PlanId.ToString()) + "&other=1");
        var cased = await client.GetAsync($"{QueueRoute(scene.RunId)}/availability?PlanProposalMessageId={scene.PlanId}");
        var malformed = await client.GetAsync(QueueAvailabilityRoute(scene.RunId, "not-a-guid"));
        var unknown = await client.GetAsync(QueueAvailabilityRoute(Guid.NewGuid(), scene.PlanId.ToString()));

        Assert.Equal(HttpStatusCode.BadRequest, missingKey.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, extra.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, cased.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }
}
