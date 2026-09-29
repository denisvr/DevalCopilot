using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DevalCopilot.Api.Features.Runs.AuthorizeReviewCorrectionWithGuidance;
using DevalCopilot.Api.Features.Runs.RequestReviewCorrection;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>
/// <c>POST …/review-correction-escalations/{id}/authorize-with-guidance</c>: protected, explicit,
/// bounded, and truthful about idempotency and conflicts. The bodyless <c>…/authorize</c> operation
/// keeps its own tests. No response or error echoes the submitted guidance.
/// </summary>
public sealed partial class ReviewCorrectionEndpointTests
{
    private const string SentinelGuidance = "Keep the fix minimal SENTINEL-4417.";

    private string AuthorizeWithGuidanceUrl(Guid runId, Guid escalationId) =>
        $"/api/runs/{runId}/review-correction-escalations/{escalationId}/authorize-with-guidance";

    private async Task<(Guid RunId, Guid EscalationId)> SeedEscalationAsync(HttpClient client)
    {
        var seed = await SeedCorrectionChainAsync(maximumAgentInvocationTime: TimeSpan.FromHours(24));
        using (var scope = _factory.Services.CreateScope())
        {
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
        }

        var escalation = await client.PostAsJsonAsync(
            $"/api/runs/{seed.RunId}/agent-attempts/review-correction", new RequestReviewCorrectionRequest(seed.ReviewId));
        using var json = JsonDocument.Parse(await escalation.Content.ReadAsStringAsync());
        return (seed.RunId, json.RootElement.GetProperty("escalationId").GetGuid());
    }

    private static StringContent Body(string guidance) =>
        new(JsonSerializer.Serialize(new AuthorizeReviewCorrectionWithGuidanceRequest(guidance)), Encoding.UTF8, "application/json");

    [Fact]
    public async Task Guided_authorization_requires_authentication()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(AuthorizeWithGuidanceUrl(Guid.NewGuid(), Guid.NewGuid()), Body("anything"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Guided_authorization_for_an_unknown_run_or_escalation_is_a_safe_not_found()
    {
        using var client = AuthenticatedClient();
        var (runId, _) = await SeedEscalationAsync(client);

        var unknownRun = await client.PostAsync(AuthorizeWithGuidanceUrl(Guid.NewGuid(), Guid.NewGuid()), Body(SentinelGuidance));
        var unknownEscalation = await client.PostAsync(AuthorizeWithGuidanceUrl(runId, Guid.NewGuid()), Body(SentinelGuidance));

        foreach (var response in new[] { unknownRun, unknownEscalation })
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.DoesNotContain("SENTINEL", body, StringComparison.Ordinal);
            AssertNoDisclosure(body);
        }
    }

    [Fact]
    public async Task Guided_authorization_without_a_body_is_rejected()
    {
        using var client = AuthenticatedClient();
        var (runId, escalationId) = await SeedEscalationAsync(client);

        var response = await client.PostAsync(AuthorizeWithGuidanceUrl(runId, escalationId), content: null);

        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnsupportedMediaType, response.StatusCode.ToString());
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        Assert.Empty(await db.ReviewCorrectionAuthorizations.ToListAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Continue only after explicit human authorization.")]
    [InlineData("SENTINEL-9001 contains the api key marker")]
    [InlineData("SENTINEL-9002 reads C:\\Users\\me")]
    public async Task Invalid_guidance_is_a_400_that_never_echoes_it_and_records_nothing(string guidance)
    {
        using var client = AuthenticatedClient();
        var (runId, escalationId) = await SeedEscalationAsync(client);

        var response = await client.PostAsync(AuthorizeWithGuidanceUrl(runId, escalationId), Body(guidance));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("review_correction_authorizations.guidance_invalid", body, StringComparison.Ordinal);
        Assert.DoesNotContain("SENTINEL", body, StringComparison.Ordinal);
        AssertNoDisclosure(body);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        Assert.Empty(await db.ReviewCorrectionAuthorizations.ToListAsync());
    }

    [Fact]
    public async Task Over_length_guidance_is_rejected_without_echo()
    {
        using var client = AuthenticatedClient();
        var (runId, escalationId) = await SeedEscalationAsync(client);

        var response = await client.PostAsync(
            AuthorizeWithGuidanceUrl(runId, escalationId), Body("SENTINEL-1 " + new string('x', ReviewCorrectionGuidance.MaximumLength)));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("SENTINEL", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_oversized_request_body_is_refused_before_it_is_read_into_guidance()
    {
        using var client = AuthenticatedClient();
        var (runId, escalationId) = await SeedEscalationAsync(client);
        var huge = new StringContent(
            JsonSerializer.Serialize(new { guidance = "SENTINEL-77 " + new string('x', 64 * 1024) }), Encoding.UTF8, "application/json");

        var response = await client.PostAsync(AuthorizeWithGuidanceUrl(runId, escalationId), huge);
        var body = await response.Content.ReadAsStringAsync();

        Assert.True((int)response.StatusCode is 400 or 413, response.StatusCode.ToString());
        Assert.DoesNotContain("SENTINEL", body, StringComparison.Ordinal);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        Assert.Empty(await db.ReviewCorrectionAuthorizations.ToListAsync());
    }

    [Fact]
    public async Task Guided_authorization_records_the_exact_guidance_returns_identity_only_and_is_idempotent_or_conflicting()
    {
        using var client = AuthenticatedClient();
        var (runId, escalationId) = await SeedEscalationAsync(client);

        var first = await client.PostAsync(AuthorizeWithGuidanceUrl(runId, escalationId), Body(SentinelGuidance));
        var firstBody = await first.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.DoesNotContain("SENTINEL", firstBody, StringComparison.Ordinal);
        AssertNoDisclosure(firstBody);
        using var firstJson = JsonDocument.Parse(firstBody);
        Assert.Equal("Authorized", firstJson.RootElement.GetProperty("status").GetString());
        Assert.Equal(5, firstJson.RootElement.EnumerateObject().Count());
        var messageId = firstJson.RootElement.GetProperty("humanInstructionMessageId").GetGuid();
        var authorizationId = firstJson.RootElement.GetProperty("authorizationId").GetGuid();

        var identical = await client.PostAsync(AuthorizeWithGuidanceUrl(runId, escalationId), Body("  " + SentinelGuidance + "  "));
        using var identicalJson = JsonDocument.Parse(await identical.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, identical.StatusCode);
        Assert.Equal(authorizationId, identicalJson.RootElement.GetProperty("authorizationId").GetGuid());

        var different = await client.PostAsync(AuthorizeWithGuidanceUrl(runId, escalationId), Body("A different SENTINEL-5150 request."));
        var differentBody = await different.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Conflict, different.StatusCode);
        Assert.Contains("review_correction_authorizations.guidance_conflict", differentBody, StringComparison.Ordinal);
        Assert.DoesNotContain("SENTINEL", differentBody, StringComparison.Ordinal);
        AssertNoDisclosure(differentBody);

        // Bodyless is "no guidance", which is different from the recorded guidance: a safe conflict too.
        var bodyless = await client.PostAsync(
            $"/api/runs/{runId}/review-correction-escalations/{escalationId}/authorize", content: null);
        var bodylessBody = await bodyless.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Conflict, bodyless.StatusCode);
        Assert.DoesNotContain("SENTINEL", bodylessBody, StringComparison.Ordinal);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var message = await db.CollaborationMessages.AsNoTracking().SingleAsync(m => m.Id == messageId);
        Assert.Equal(SentinelGuidance, ReviewCorrectionGuidance.TryReadRationale(message.StructuredContentJson));
        Assert.Single(await db.ReviewCorrectionAuthorizations.ToListAsync());
        Assert.Single(await db.CollaborationMessages.Where(m => m.Type == CollaborationMessageType.HumanInstruction).ToListAsync());
    }
}
