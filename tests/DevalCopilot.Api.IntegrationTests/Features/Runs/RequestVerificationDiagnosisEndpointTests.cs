using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DevalCopilot.Api.Features.Runs.RequestVerificationDiagnosis;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>
/// Exercises <c>POST /api/runs/{runId}/agent-attempts/verification-diagnosis</c> against the real Api host: authentication,
/// the golden path with its pinned rows, and the distinct refusals of the failed-verification selection. The exhaustive
/// selection and authority permutations live at the handler level; this proves the protected MVC contract and that no response
/// discloses a path, an argument, an output line, or a fingerprint. Seeded rows are raw evidence, not production-written.
/// </summary>
public sealed class RequestVerificationDiagnosisEndpointTests : IDisposable
{
    private readonly CodeReviewApiWebApplicationFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private HttpClient CreateAuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return client;
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, Guid runId, Guid reportId) =>
        client.PostAsJsonAsync($"/api/runs/{runId}/agent-attempts/verification-diagnosis", new RequestVerificationDiagnosisRequest(reportId));

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
        AssertNoDisclosure(body);
    }

    [Fact]
    public async Task Returns_a_safe_not_found_response_for_an_unknown_execution_report()
    {
        var seed = await VerificationDiagnosisApiSeed.SeedImplementedRunAsync(_factory);
        using var client = CreateAuthenticatedClient();

        var response = await PostAsync(client, seed.RunId, Guid.NewGuid());
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("agent_attempts.execution_report_not_found", body, StringComparison.Ordinal);
        AssertNoDisclosure(body);
    }

    [Fact]
    public async Task Returns_conflict_when_no_verification_command_is_enabled()
    {
        var seed = await VerificationDiagnosisApiSeed.SeedImplementedRunAsync(_factory);
        using var client = CreateAuthenticatedClient();

        var response = await PostAsync(client, seed.RunId, seed.ReportId);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("verification_diagnosis.no_verification_commands_enabled", body, StringComparison.Ordinal);
        AssertNoDisclosure(body);
    }

    [Fact]
    public async Task Returns_conflict_when_every_enabled_command_passed_so_there_is_nothing_to_diagnose()
    {
        var seed = await VerificationDiagnosisApiSeed.SeedImplementedRunAsync(_factory);
        await VerificationDiagnosisApiSeed.SeedVerificationAsync(_factory, seed, 1, 0);
        using var client = CreateAuthenticatedClient();

        var response = await PostAsync(client, seed.RunId, seed.ReportId);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("verification_diagnosis.no_failed_verification", body, StringComparison.Ordinal);
        AssertNoDisclosure(body);
        await AssertNoAttemptRowsAsync(seed.RunId);
    }

    [Fact]
    public async Task Succeeds_returns_the_attempt_identity_only_and_pins_the_complete_ordered_verification()
    {
        var seed = await VerificationDiagnosisApiSeed.SeedImplementedRunAsync(_factory);
        var passed = await VerificationDiagnosisApiSeed.SeedVerificationAsync(_factory, seed, 1, 0);
        var failed = await VerificationDiagnosisApiSeed.SeedVerificationAsync(_factory, seed, 2, 1);
        using var client = CreateAuthenticatedClient();

        var response = await PostAsync(client, seed.RunId, seed.ReportId);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<RequestVerificationDiagnosisResponse>();
        Assert.NotNull(payload);
        AssertNoDisclosure(body);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var attempt = await dbContext.Attempts.SingleAsync(candidate => candidate.Id == payload!.AttemptId);
        Assert.Equal(AgentResponseContract.VerificationDiagnosis, attempt.AgentResponseContract);
        Assert.Equal(AgentRole.CodeReviewer, attempt.AgentRole);
        Assert.Equal(AgentPermissionProfile.ReadOnly, attempt.AgentPermissionProfile);
        Assert.Equal(VerificationDiagnosisPolicy.AdapterContractVersion, attempt.AgentAdapterContractVersion);
        Assert.Null(attempt.AgentRepairSourceAttemptId);
        Assert.Equal(
            [seed.ReportId],
            await dbContext.AttemptInputMessages.Where(input => input.AttemptId == attempt.Id).Select(input => input.CollaborationMessageId).ToListAsync());
        Assert.Equal(
            [passed.ExecutionId, failed.ExecutionId],
            await dbContext.AttemptVerificationEvidence.Where(evidence => evidence.AttemptId == attempt.Id)
                .OrderBy(evidence => evidence.Sequence).Select(evidence => evidence.VerificationExecutionId).ToListAsync());
    }

    [Fact]
    public async Task A_second_request_is_refused_while_the_first_attempt_is_active()
    {
        var seed = await VerificationDiagnosisApiSeed.SeedImplementedRunAsync(_factory);
        await VerificationDiagnosisApiSeed.SeedVerificationAsync(_factory, seed, 1, 1);
        using var client = CreateAuthenticatedClient();
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(client, seed.RunId, seed.ReportId)).StatusCode);

        var response = await PostAsync(client, seed.RunId, seed.ReportId);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("attempts.run_has_active_attempt", body, StringComparison.Ordinal);
    }

    private async Task AssertNoAttemptRowsAsync(Guid runId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        Assert.Empty(await dbContext.Attempts.Where(attempt => attempt.RunId == runId && attempt.AgentResponseContract == AgentResponseContract.VerificationDiagnosis).ToListAsync());
    }

    internal static void AssertNoDisclosure(string body)
    {
        Assert.DoesNotContain("C:\\", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(CodeReviewApiWebApplicationFactory.MatchingFingerprint, body, StringComparison.Ordinal);
        Assert.DoesNotContain(VerificationDiagnosisApiSeed.SentinelExecutable, body, StringComparison.Ordinal);
        Assert.DoesNotContain(VerificationDiagnosisApiSeed.SentinelArgument, body, StringComparison.Ordinal);
        Assert.DoesNotContain(VerificationDiagnosisApiSeed.SentinelOutput, body, StringComparison.Ordinal);
        Assert.DoesNotContain("resolvedExecutablePath", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("contentHash", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
        Assert.DoesNotContain("System.", body, StringComparison.Ordinal);
    }
}
