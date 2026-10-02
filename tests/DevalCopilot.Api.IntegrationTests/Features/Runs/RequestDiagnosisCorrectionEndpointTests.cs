using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DevalCopilot.Api.Features.Runs.RequestDiagnosisCorrection;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>Exercises <c>POST /api/runs/{runId}/agent-attempts/verification-diagnosis/correction</c>: authentication, safe
/// refusals, the claim of a diagnosis-origin correction with its exact ordered inputs, and the exhaustion escalation that creates
/// no attempt. Seeded rows are raw evidence, not production-written.</summary>
public sealed class RequestDiagnosisCorrectionEndpointTests : IDisposable
{
    private readonly CodeReviewApiWebApplicationFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private HttpClient CreateAuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return client;
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, Guid runId, Guid diagnosisAttemptId) =>
        client.PostAsJsonAsync(
            $"/api/runs/{runId}/agent-attempts/verification-diagnosis/correction", new RequestDiagnosisCorrectionRequest(diagnosisAttemptId));

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

    [Fact]
    public async Task Requires_authentication()
    {
        using var client = _factory.CreateClient();

        var response = await PostAsync(client, Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_a_safe_not_found_response_for_an_unknown_run()
    {
        using var client = CreateAuthenticatedClient();

        var response = await PostAsync(client, Guid.NewGuid(), Guid.NewGuid());
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("runs.not_found", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refuses_an_unknown_diagnosis_without_creating_anything()
    {
        var seed = await VerificationDiagnosisApiSeed.SeedImplementedRunAsync(_factory);
        await ObserveClaudeAsync();
        using var client = CreateAuthenticatedClient();

        var response = await PostAsync(client, seed.RunId, Guid.NewGuid());
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("agent_attempts.diagnosis_not_applicable", body, StringComparison.Ordinal);
        RequestVerificationDiagnosisEndpointTests.AssertNoDisclosure(body);
        await AssertCorrectionAttemptCountAsync(seed.RunId, 0);
    }

    [Fact]
    public async Task Claims_a_correction_whose_inputs_are_the_report_then_every_finding_in_order()
    {
        var seed = await VerificationDiagnosisApiSeed.SeedImplementedRunAsync(_factory);
        var failed = await VerificationDiagnosisApiSeed.SeedVerificationAsync(_factory, seed, 1, 1);
        var (diagnosisId, findingIds) = await VerificationDiagnosisApiSeed.SeedCompletedDiagnosisAsync(_factory, seed, [failed], 3, attemptNumber: 4);
        await ObserveClaudeAsync();
        using var client = CreateAuthenticatedClient();

        var response = await PostAsync(client, seed.RunId, diagnosisId);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<RequestDiagnosisCorrectionResponse>();
        Assert.Equal("AttemptCreated", payload!.Status);
        Assert.NotNull(payload.AttemptId);
        RequestVerificationDiagnosisEndpointTests.AssertNoDisclosure(body);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var attempt = await dbContext.Attempts.SingleAsync(candidate => candidate.Id == payload.AttemptId);
        Assert.Equal(AgentResponseContract.ReviewCorrection, attempt.AgentResponseContract);
        Assert.Equal(
            [seed.ReportId, .. findingIds],
            await dbContext.AttemptInputMessages.Where(input => input.AttemptId == attempt.Id)
                .OrderBy(input => input.Sequence).Select(input => input.CollaborationMessageId).ToListAsync());
    }

    [Fact]
    public async Task Records_one_escalation_without_an_attempt_when_the_shared_allowance_is_spent_and_repeats_idempotently()
    {
        var seed = await VerificationDiagnosisApiSeed.SeedImplementedRunAsync(_factory);
        var failed = await VerificationDiagnosisApiSeed.SeedVerificationAsync(_factory, seed, 1, 1);
        var (diagnosisId, _) = await VerificationDiagnosisApiSeed.SeedCompletedDiagnosisAsync(_factory, seed, [failed], 1, attemptNumber: 4);
        await ObserveClaudeAsync();
        await VerificationDiagnosisApiSeed.SpendCorrectionAllowanceAsync(_factory, seed);
        using var client = CreateAuthenticatedClient();

        var first = await (await PostAsync(client, seed.RunId, diagnosisId)).Content.ReadFromJsonAsync<RequestDiagnosisCorrectionResponse>();
        var second = await (await PostAsync(client, seed.RunId, diagnosisId)).Content.ReadFromJsonAsync<RequestDiagnosisCorrectionResponse>();

        Assert.Equal("Escalated", first!.Status);
        Assert.Null(first.AttemptId);
        Assert.NotNull(first.EscalationId);
        Assert.Equal(first.EscalationId, second!.EscalationId);
        Assert.Equal(first.EscalationMessageId, second.EscalationMessageId);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        Assert.Single(await dbContext.DiagnosisCorrectionEscalations.Where(escalation => escalation.RunId == seed.RunId).ToListAsync());
        Assert.Empty(await dbContext.ReviewCorrectionEscalations.Where(escalation => escalation.RunId == seed.RunId).ToListAsync());
        Assert.Empty(await dbContext.ReviewCorrectionAuthorizations.ToListAsync());
    }

    private async Task AssertCorrectionAttemptCountAsync(Guid runId, int expected)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        Assert.Equal(
            expected,
            await dbContext.Attempts.CountAsync(attempt => attempt.RunId == runId && attempt.AgentResponseContract == AgentResponseContract.ReviewCorrection));
    }
}
