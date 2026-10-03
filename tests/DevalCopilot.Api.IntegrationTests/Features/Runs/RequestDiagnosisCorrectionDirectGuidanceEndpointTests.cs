using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DevalCopilot.Api.Features.Runs.RequestDiagnosisCorrection;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>
/// <c>POST …/agent-attempts/verification-diagnosis/correction</c> with optional direct human guidance (ADR-0019): same route and
/// response shape, the guidance bounded and never echoed, available only within the shared correction allowance (a refusal at
/// exhaustion creates no escalation), and the recorded fact exposed by the diagnosis status, cockpit and attempt-evidence
/// projections. Seeded rows are raw evidence, not production-written.
/// </summary>
public sealed class RequestDiagnosisCorrectionDirectGuidanceEndpointTests : IDisposable
{
    private const string Guidance = "Keep the fix minimal SENTINEL-6200.\nReuse the existing helper.";

    private readonly CodeReviewApiWebApplicationFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private HttpClient AuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return client;
    }

    private static string Url(Guid runId) => $"/api/runs/{runId}/agent-attempts/verification-diagnosis/correction";

    private static StringContent RawBody(string json) => new(json, Encoding.UTF8, "application/json");

    private async Task ObserveClaudeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;
        var claude = await dbContext.HostCapabilitySnapshots.SingleAsync(snapshot => snapshot.Capability == Capability.ClaudeCli);
        claude.MarkDispatched(now);
        claude.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\safe\claude.exe", null, "2.1.0", now, now.AddMinutes(5));
        await dbContext.SaveChangesAsync();
    }

    private async Task<(VerificationDiagnosisApiSeed.Run Seed, Guid DiagnosisId)> SeedDiagnosisAsync(bool spendAllowance = false)
    {
        var seed = await VerificationDiagnosisApiSeed.SeedImplementedRunAsync(_factory);
        var failed = await VerificationDiagnosisApiSeed.SeedVerificationAsync(_factory, seed, 1, 1);
        var (diagnosisId, _) = await VerificationDiagnosisApiSeed.SeedCompletedDiagnosisAsync(_factory, seed, [failed], 2, attemptNumber: 4);
        await ObserveClaudeAsync();
        if (spendAllowance)
        {
            await VerificationDiagnosisApiSeed.SpendCorrectionAllowanceAsync(_factory, seed);
        }

        return (seed, diagnosisId);
    }

    private async Task<(int Escalations, int Attempts, int Manifests)> CountsAsync(Guid runId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        return (
            await db.DiagnosisCorrectionEscalations.CountAsync(e => e.RunId == runId),
            await db.Attempts.CountAsync(a => a.RunId == runId),
            await db.Artifacts.CountAsync(a => a.RunId == runId && a.Purpose == ArtifactPurpose.AgentContextManifest));
    }

    [Fact]
    public async Task A_guided_post_requires_authentication()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(Url(Guid.NewGuid()), new RequestDiagnosisCorrectionRequest(Guid.NewGuid(), Guidance));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_guided_request_keeps_the_response_shape_never_echoes_the_text_and_is_projected_exactly()
    {
        var (seed, diagnosisId) = await SeedDiagnosisAsync();
        using var client = AuthenticatedClient();
        var beforeStatus = JsonDocument.Parse(await (await client.GetAsync($"/api/runs/{seed.RunId}/agent-attempts/verification-diagnosis")).Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, beforeStatus.RootElement.GetProperty("correctionDirectGuidance").ValueKind);

        var post = await client.PostAsync(
            Url(seed.RunId),
            RawBody(JsonSerializer.Serialize(new { verificationDiagnosisAttemptId = diagnosisId, guidance = "  " + Guidance.Replace("\n", "\r\n", StringComparison.Ordinal) + "  " })));
        var postBody = await post.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        Assert.DoesNotContain("SENTINEL", postBody, StringComparison.Ordinal);
        RequestVerificationDiagnosisEndpointTests.AssertNoDisclosure(postBody);
        using var created = JsonDocument.Parse(postBody);
        Assert.Equal("AttemptCreated", created.RootElement.GetProperty("status").GetString());
        var attemptId = created.RootElement.GetProperty("attemptId").GetGuid();
        Assert.Equal(
            new[] { "attemptId", "attemptNumber", "escalationId", "escalationMessageId", "latestEventSequence", "status" },
            created.RootElement.EnumerateObject().Select(property => property.Name).Order().ToArray());

        var statusBody = await (await client.GetAsync($"/api/runs/{seed.RunId}/agent-attempts/verification-diagnosis")).Content.ReadAsStringAsync();
        using var status = JsonDocument.Parse(statusBody);
        var fact = status.RootElement.GetProperty("correctionDirectGuidance");
        Assert.Equal("Provided", fact.GetProperty("state").GetString());
        Assert.Equal(Guidance, fact.GetProperty("text").GetString());
        Assert.Equal(attemptId, status.RootElement.GetProperty("correctionAttemptId").GetGuid());
        Assert.Equal(JsonValueKind.Null, status.RootElement.GetProperty("correctionEscalationId").ValueKind);

        var cockpit = JsonDocument.Parse(await (await client.GetAsync($"/api/runs/{seed.RunId}/cockpit")).Content.ReadAsStringAsync());
        Assert.Equal(Guidance, cockpit.RootElement.GetProperty("latestAgentAttempt").GetProperty("directGuidance").GetProperty("text").GetString());
        var evidence = JsonDocument.Parse(await (await client.GetAsync($"/api/runs/{seed.RunId}/agent-attempts/{attemptId}/evidence")).Content.ReadAsStringAsync());
        Assert.Equal("Provided", evidence.RootElement.GetProperty("directGuidance").GetProperty("state").GetString());
        Assert.Equal(Guidance, evidence.RootElement.GetProperty("directGuidance").GetProperty("text").GetString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        Assert.Empty(await db.DiagnosisCorrectionEscalations.ToListAsync());
        Assert.Empty(await db.ReviewCorrectionAuthorizations.ToListAsync());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("omitted")]
    public async Task A_null_or_omitted_guidance_preserves_the_existing_behavior(string form)
    {
        var (seed, diagnosisId) = await SeedDiagnosisAsync();
        using var client = AuthenticatedClient();
        var json = form == "null"
            ? $"{{\"verificationDiagnosisAttemptId\":\"{diagnosisId}\",\"guidance\":null}}"
            : $"{{\"verificationDiagnosisAttemptId\":\"{diagnosisId}\"}}";

        var post = await client.PostAsync(Url(seed.RunId), RawBody(json));

        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        var status = JsonDocument.Parse(await (await client.GetAsync($"/api/runs/{seed.RunId}/agent-attempts/verification-diagnosis")).Content.ReadAsStringAsync());
        Assert.Equal("NotRecorded", status.RootElement.GetProperty("correctionDirectGuidance").GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, status.RootElement.GetProperty("correctionDirectGuidance").GetProperty("text").ValueKind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("tab\\there SENTINEL-7101")]
    [InlineData("SENTINEL-7102 contains the api key marker")]
    [InlineData("SENTINEL-7103 reads C:\\\\Users\\\\me")]
    public async Task Invalid_guidance_is_a_400_that_never_echoes_it_and_claims_nothing(string escapedGuidance)
    {
        var (seed, diagnosisId) = await SeedDiagnosisAsync();
        using var client = AuthenticatedClient();
        var before = await CountsAsync(seed.RunId);

        var response = await client.PostAsync(
            Url(seed.RunId), RawBody($"{{\"verificationDiagnosisAttemptId\":\"{diagnosisId}\",\"guidance\":\"{escapedGuidance}\"}}"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("agent_attempts.direct_guidance_invalid", body, StringComparison.Ordinal);
        Assert.DoesNotContain("SENTINEL", body, StringComparison.Ordinal);
        RequestVerificationDiagnosisEndpointTests.AssertNoDisclosure(body);
        Assert.Equal(before, await CountsAsync(seed.RunId));
    }

    [Fact]
    public async Task An_over_long_guidance_is_a_400_and_an_oversized_body_is_rejected_before_any_work()
    {
        var (seed, diagnosisId) = await SeedDiagnosisAsync();
        using var client = AuthenticatedClient();
        var before = await CountsAsync(seed.RunId);

        var overlong = await client.PostAsJsonAsync(Url(seed.RunId), new RequestDiagnosisCorrectionRequest(diagnosisId, new string('x', 601)));
        var oversized = await client.PostAsJsonAsync(Url(seed.RunId), new RequestDiagnosisCorrectionRequest(diagnosisId, new string('x', 9 * 1024)));

        Assert.Equal(HttpStatusCode.BadRequest, overlong.StatusCode);
        Assert.True(oversized.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode.ToString());
        Assert.Equal(before, await CountsAsync(seed.RunId));
    }

    [Fact]
    public async Task A_foreign_diagnosis_with_guidance_is_the_same_safe_conflict_as_without_it()
    {
        var (seed, _) = await SeedDiagnosisAsync();
        using var client = AuthenticatedClient();

        var plain = await client.PostAsJsonAsync(Url(seed.RunId), new RequestDiagnosisCorrectionRequest(Guid.NewGuid()));
        var guided = await client.PostAsJsonAsync(Url(seed.RunId), new RequestDiagnosisCorrectionRequest(Guid.NewGuid(), Guidance));

        Assert.Equal(plain.StatusCode, guided.StatusCode);
        var guidedBody = await guided.Content.ReadAsStringAsync();
        Assert.Contains("agent_attempts.diagnosis_not_applicable", guidedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("SENTINEL", guidedBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task At_exhaustion_a_guided_request_is_a_409_that_creates_no_escalation_while_the_bodyless_flow_is_unchanged()
    {
        var (seed, diagnosisId) = await SeedDiagnosisAsync(spendAllowance: true);
        using var client = AuthenticatedClient();
        var before = await CountsAsync(seed.RunId);

        var first = await client.PostAsJsonAsync(Url(seed.RunId), new RequestDiagnosisCorrectionRequest(diagnosisId, Guidance));
        var firstBody = await first.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, first.StatusCode);
        Assert.Contains("agent_attempts.direct_guidance_unavailable", firstBody, StringComparison.Ordinal);
        Assert.DoesNotContain("SENTINEL", firstBody, StringComparison.Ordinal);
        Assert.Equal(before, await CountsAsync(seed.RunId));
        Assert.Equal(0, (await CountsAsync(seed.RunId)).Escalations);

        var escalate = await client.PostAsJsonAsync(Url(seed.RunId), new RequestDiagnosisCorrectionRequest(diagnosisId));
        using var escalated = JsonDocument.Parse(await escalate.Content.ReadAsStringAsync());
        Assert.Equal("Escalated", escalated.RootElement.GetProperty("status").GetString());
        var afterEscalation = await CountsAsync(seed.RunId);
        Assert.Equal(1, afterEscalation.Escalations);

        var guidedAgain = await client.PostAsJsonAsync(Url(seed.RunId), new RequestDiagnosisCorrectionRequest(diagnosisId, Guidance));
        Assert.Equal(HttpStatusCode.Conflict, guidedAgain.StatusCode);
        Assert.Equal(afterEscalation, await CountsAsync(seed.RunId));
    }
}
