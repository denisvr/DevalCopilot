using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DevalCopilot.Api.Features.Runs.RequestReviewCorrection;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>
/// <c>POST …/agent-attempts/review-correction</c> with optional direct human guidance: same route and response shape, the
/// guidance bounded and never echoed, available only within the ordinary correction budget (a refusal at exhaustion
/// creates no escalation and consumes no authorization), and the recorded fact exposed by the status, cockpit and
/// evidence projections with the full review lineage.
/// </summary>
public sealed partial class ReviewCorrectionEndpointTests
{
    private const string DirectGuidance = "Keep the fix minimal SENTINEL-6120.\nReuse the existing helper.";

    private string CorrectionUrl(Guid runId) => $"/api/runs/{runId}/agent-attempts/review-correction";

    private static StringContent RawBody(string json) => new(json, Encoding.UTF8, "application/json");

    private async Task<Guid> SeedExhaustedAsync()
    {
        var seed = await SeedCorrectionChainAsync(maximumAgentInvocationTime: TimeSpan.FromHours(24));
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var review = await db.Attempts.SingleAsync(attempt => attempt.Id == seed.ReviewId);
        for (var number = 5; number <= 6; number++)
        {
            var correction = Attempt.ClaimAgentReviewCorrection(
                Guid.NewGuid(), seed.RunId, number, review.AgentGitWorkspaceId!.Value,
                review.AgentGitCheckpointId!.Value, Fingerprint, Guid.NewGuid(),
                TimeSpan.FromMinutes(20), 262144, 524288, DateTimeOffset.UtcNow, number);
            correction.MarkAgentDispatched(DateTimeOffset.UtcNow);
            correction.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, DateTimeOffset.UtcNow);
            db.Attempts.Add(correction);
        }

        await db.SaveChangesAsync();
        return seed.RunId;
    }

    private async Task<(int Escalations, int Attempts, int Manifests, int Consumed)> CountsAsync(Guid runId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        return (
            await db.ReviewCorrectionEscalations.CountAsync(e => e.RunId == runId),
            await db.Attempts.CountAsync(a => a.RunId == runId),
            await db.Artifacts.CountAsync(a => a.RunId == runId && a.Purpose == ArtifactPurpose.AgentContextManifest),
            await db.ReviewCorrectionAuthorizations.CountAsync(a => a.RunId == runId && a.ConsumedByAttemptId != null));
    }

    [Fact]
    public async Task A_guided_post_requires_authentication()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(CorrectionUrl(Guid.NewGuid()), new RequestReviewCorrectionRequest(Guid.NewGuid(), DirectGuidance));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_guided_request_keeps_the_response_shape_never_echoes_the_text_and_is_projected_exactly()
    {
        var seed = await SeedCorrectionChainAsync();
        using var client = AuthenticatedClient();

        var post = await client.PostAsync(
            CorrectionUrl(seed.RunId),
            RawBody(JsonSerializer.Serialize(new { implementationReviewAttemptId = seed.ReviewId, guidance = "  " + DirectGuidance.Replace("\n", "\r\n", StringComparison.Ordinal) + "  " })));
        var postBody = await post.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        Assert.DoesNotContain("SENTINEL", postBody, StringComparison.Ordinal);
        AssertNoDisclosure(postBody);
        using var created = JsonDocument.Parse(postBody);
        Assert.Equal("AttemptCreated", created.RootElement.GetProperty("status").GetString());
        var attemptId = created.RootElement.GetProperty("attemptId").GetGuid();
        Assert.Equal(
            new[] { "attemptId", "attemptNumber", "escalationId", "escalationMessageId", "latestEventSequence", "status" },
            created.RootElement.EnumerateObject().Select(property => property.Name).Order().ToArray());

        var statusBody = await (await client.GetAsync(CorrectionUrl(seed.RunId))).Content.ReadAsStringAsync();
        using var status = JsonDocument.Parse(statusBody);
        var fact = status.RootElement.GetProperty("directGuidance");
        Assert.Equal("Provided", fact.GetProperty("state").GetString());
        Assert.Equal(DirectGuidance, fact.GetProperty("text").GetString());
        Assert.False(status.RootElement.GetProperty("hasAvailableHumanAuthorization").GetBoolean());

        var cockpit = JsonDocument.Parse(await (await client.GetAsync($"/api/runs/{seed.RunId}/cockpit")).Content.ReadAsStringAsync());
        Assert.Equal(DirectGuidance, cockpit.RootElement.GetProperty("latestAgentAttempt").GetProperty("directGuidance").GetProperty("text").GetString());
        var evidence = JsonDocument.Parse(await (await client.GetAsync($"/api/runs/{seed.RunId}/agent-attempts/{attemptId}/evidence")).Content.ReadAsStringAsync());
        Assert.Equal("Provided", evidence.RootElement.GetProperty("directGuidance").GetProperty("state").GetString());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("omitted")]
    public async Task A_null_or_omitted_guidance_preserves_the_existing_behavior(string form)
    {
        var seed = await SeedCorrectionChainAsync();
        using var client = AuthenticatedClient();
        var json = form == "null"
            ? $"{{\"implementationReviewAttemptId\":\"{seed.ReviewId}\",\"guidance\":null}}"
            : $"{{\"implementationReviewAttemptId\":\"{seed.ReviewId}\"}}";

        var post = await client.PostAsync(CorrectionUrl(seed.RunId), RawBody(json));

        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        var status = JsonDocument.Parse(await (await client.GetAsync(CorrectionUrl(seed.RunId))).Content.ReadAsStringAsync());
        Assert.Equal("NotRecorded", status.RootElement.GetProperty("directGuidance").GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, status.RootElement.GetProperty("directGuidance").GetProperty("text").ValueKind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("tab\\there SENTINEL-7001")]
    [InlineData("SENTINEL-7002 contains the api key marker")]
    [InlineData("SENTINEL-7003 reads C:\\\\Users\\\\me")]
    public async Task Invalid_guidance_is_a_400_that_never_echoes_it_and_claims_nothing(string escapedGuidance)
    {
        var seed = await SeedCorrectionChainAsync();
        using var client = AuthenticatedClient();
        var before = await CountsAsync(seed.RunId);

        var response = await client.PostAsync(
            CorrectionUrl(seed.RunId),
            RawBody($"{{\"implementationReviewAttemptId\":\"{seed.ReviewId}\",\"guidance\":\"{escapedGuidance}\"}}"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("agent_attempts.direct_guidance_invalid", body, StringComparison.Ordinal);
        Assert.DoesNotContain("SENTINEL", body, StringComparison.Ordinal);
        AssertNoDisclosure(body);
        Assert.Equal(before, await CountsAsync(seed.RunId));
    }

    [Fact]
    public async Task An_over_long_guidance_is_a_400_and_an_oversized_body_is_rejected_before_any_work()
    {
        var seed = await SeedCorrectionChainAsync();
        using var client = AuthenticatedClient();
        var before = await CountsAsync(seed.RunId);

        var overlong = await client.PostAsJsonAsync(CorrectionUrl(seed.RunId), new RequestReviewCorrectionRequest(seed.ReviewId, new string('x', 601)));
        var oversized = await client.PostAsJsonAsync(CorrectionUrl(seed.RunId), new RequestReviewCorrectionRequest(seed.ReviewId, new string('x', 9 * 1024)));

        Assert.Equal(HttpStatusCode.BadRequest, overlong.StatusCode);
        Assert.True(oversized.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode.ToString());
        Assert.Equal(before, await CountsAsync(seed.RunId));
    }

    [Fact]
    public async Task A_foreign_review_with_guidance_is_the_same_safe_conflict_as_without_it()
    {
        var seed = await SeedCorrectionChainAsync();
        using var client = AuthenticatedClient();

        var plain = await client.PostAsJsonAsync(CorrectionUrl(seed.RunId), new RequestReviewCorrectionRequest(Guid.NewGuid()));
        var guided = await client.PostAsJsonAsync(CorrectionUrl(seed.RunId), new RequestReviewCorrectionRequest(Guid.NewGuid(), DirectGuidance));

        Assert.Equal(plain.StatusCode, guided.StatusCode);
        var guidedBody = await guided.Content.ReadAsStringAsync();
        Assert.Contains("agent_attempts.review_not_applicable", guidedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("SENTINEL", guidedBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task At_exhaustion_a_guided_request_is_a_409_that_creates_no_escalation_and_consumes_no_authorization()
    {
        var runId = await SeedExhaustedAsync();
        var review = await ReviewIdAsync(runId);
        using var client = AuthenticatedClient();
        var before = await CountsAsync(runId);

        var first = await client.PostAsJsonAsync(CorrectionUrl(runId), new RequestReviewCorrectionRequest(review, DirectGuidance));
        var firstBody = await first.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, first.StatusCode);
        Assert.Contains("agent_attempts.direct_guidance_unavailable", firstBody, StringComparison.Ordinal);
        Assert.DoesNotContain("SENTINEL", firstBody, StringComparison.Ordinal);
        Assert.Equal(before, await CountsAsync(runId));
        Assert.Equal(0, (await CountsAsync(runId)).Escalations);

        // The existing bodyless flow is unchanged: it creates the escalation, and a guided retry still creates nothing more.
        var escalate = await client.PostAsJsonAsync(CorrectionUrl(runId), new RequestReviewCorrectionRequest(review));
        using var escalated = JsonDocument.Parse(await escalate.Content.ReadAsStringAsync());
        Assert.Equal("Escalated", escalated.RootElement.GetProperty("status").GetString());
        var escalationId = escalated.RootElement.GetProperty("escalationId").GetGuid();
        var afterEscalation = await CountsAsync(runId);
        Assert.Equal(1, afterEscalation.Escalations);

        var guidedAgain = await client.PostAsJsonAsync(CorrectionUrl(runId), new RequestReviewCorrectionRequest(review, DirectGuidance));
        Assert.Equal(HttpStatusCode.Conflict, guidedAgain.StatusCode);
        Assert.Equal(afterEscalation, await CountsAsync(runId));

        var authorize = await client.PostAsync($"/api/runs/{runId}/review-correction-escalations/{escalationId}/authorize", content: null);
        Assert.Equal(HttpStatusCode.OK, authorize.StatusCode);
        var status = JsonDocument.Parse(await (await client.GetAsync(CorrectionUrl(runId))).Content.ReadAsStringAsync());
        Assert.True(status.RootElement.GetProperty("hasAvailableHumanAuthorization").GetBoolean());

        var guidedWithAuthorization = await client.PostAsJsonAsync(CorrectionUrl(runId), new RequestReviewCorrectionRequest(review, DirectGuidance));
        Assert.Equal(HttpStatusCode.Conflict, guidedWithAuthorization.StatusCode);
        Assert.Contains("agent_attempts.direct_guidance_unavailable", await guidedWithAuthorization.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var afterRefusal = await CountsAsync(runId);
        Assert.Equal(0, afterRefusal.Consumed);
        Assert.Equal(afterEscalation.Attempts, afterRefusal.Attempts);
        Assert.Equal(0, afterRefusal.Manifests);

        // The authorization is intact: a bodyless request still consumes it and claims the attempt.
        var plain = await client.PostAsJsonAsync(CorrectionUrl(runId), new RequestReviewCorrectionRequest(review));
        using var claimed = JsonDocument.Parse(await plain.Content.ReadAsStringAsync());
        Assert.Equal("AttemptCreated", claimed.RootElement.GetProperty("status").GetString());
        Assert.Equal(1, (await CountsAsync(runId)).Consumed);
    }

    private async Task<Guid> ReviewIdAsync(Guid runId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        return await db.Attempts.Where(a => a.RunId == runId && a.AgentResponseContract == AgentResponseContract.ImplementationReview)
            .Select(a => a.Id).SingleAsync();
    }
}
