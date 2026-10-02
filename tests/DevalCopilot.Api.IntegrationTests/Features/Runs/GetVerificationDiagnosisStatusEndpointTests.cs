using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>Exercises <c>GET /api/runs/{runId}/agent-attempts/verification-diagnosis</c>: authentication, the explicit
/// no-attempt body with its display hint, the populated projection of a completed findings diagnosis (pinned verification list,
/// correction facts), and that nothing disclosed is a path, an argument, an output line, or a hash.</summary>
public sealed class GetVerificationDiagnosisStatusEndpointTests : IDisposable
{
    private readonly CodeReviewApiWebApplicationFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private HttpClient CreateAuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return client;
    }

    [Fact]
    public async Task Requires_authentication()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/runs/{Guid.NewGuid()}/agent-attempts/verification-diagnosis");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_a_safe_not_found_response_for_an_unknown_run()
    {
        using var client = CreateAuthenticatedClient();

        var response = await client.GetAsync($"/api/runs/{Guid.NewGuid()}/agent-attempts/verification-diagnosis");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("runs.not_found", body, StringComparison.Ordinal);
        RequestVerificationDiagnosisEndpointTests.AssertNoDisclosure(body);
    }

    [Fact]
    public async Task Returns_has_attempt_false_with_the_diagnosable_hint_for_a_run_that_never_requested_a_diagnosis()
    {
        var seed = await VerificationDiagnosisApiSeed.SeedImplementedRunAsync(_factory);
        await VerificationDiagnosisApiSeed.SeedVerificationAsync(_factory, seed, 1, 1);
        using var client = CreateAuthenticatedClient();

        var response = await client.GetAsync($"/api/runs/{seed.RunId}/agent-attempts/verification-diagnosis");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.False(root.GetProperty("hasAttempt").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("attemptId").ValueKind);
        Assert.Empty(root.GetProperty("verification").EnumerateArray());
        Assert.Equal(seed.ReportId, root.GetProperty("diagnosableExecutionReportMessageId").GetGuid());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("diagnosisUnavailableCode").ValueKind);
        RequestVerificationDiagnosisEndpointTests.AssertNoDisclosure(body);
    }

    [Fact]
    public async Task Names_why_the_current_verification_cannot_be_diagnosed()
    {
        var seed = await VerificationDiagnosisApiSeed.SeedImplementedRunAsync(_factory);
        await VerificationDiagnosisApiSeed.SeedVerificationAsync(_factory, seed, 1, 0);
        using var client = CreateAuthenticatedClient();

        var body = await client.GetStringAsync($"/api/runs/{seed.RunId}/agent-attempts/verification-diagnosis");

        using var document = JsonDocument.Parse(body);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("diagnosableExecutionReportMessageId").ValueKind);
        Assert.Equal("verification_diagnosis.no_failed_verification", document.RootElement.GetProperty("diagnosisUnavailableCode").GetString());
    }

    [Fact]
    public async Task Returns_the_populated_projection_of_a_completed_findings_diagnosis()
    {
        var seed = await VerificationDiagnosisApiSeed.SeedImplementedRunAsync(_factory);
        var passed = await VerificationDiagnosisApiSeed.SeedVerificationAsync(_factory, seed, 1, 0);
        var failed = await VerificationDiagnosisApiSeed.SeedVerificationAsync(_factory, seed, 2, 2);
        var (attemptId, _) = await VerificationDiagnosisApiSeed.SeedCompletedDiagnosisAsync(_factory, seed, [passed, failed], 2, attemptNumber: 4);
        using var client = CreateAuthenticatedClient();

        var response = await client.GetAsync($"/api/runs/{seed.RunId}/agent-attempts/verification-diagnosis");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.True(root.GetProperty("hasAttempt").GetBoolean());
        Assert.Equal(attemptId, root.GetProperty("attemptId").GetGuid());
        Assert.Equal(seed.ReportId, root.GetProperty("executionReportMessageId").GetGuid());
        Assert.Equal("Completed", root.GetProperty("status").GetString());
        Assert.Equal("DiagnosisFindingsRecorded", root.GetProperty("outcome").GetString());
        Assert.Equal(2, root.GetProperty("findingCount").GetInt32());
        Assert.True(root.GetProperty("correctionApplicable").GetBoolean());
        Assert.Equal("read-only", root.GetProperty("configuredCommandSandbox").GetString());
        Assert.Equal("Disabled", root.GetProperty("configuredRolloutPersistence").GetString());

        var members = root.GetProperty("verification").EnumerateArray().ToArray();
        Assert.Equal(2, members.Length);
        Assert.Equal(new[] { "Passed", "Failed" }, members.Select(member => member.GetProperty("status").GetString()!).ToArray());
        Assert.Equal([0, 2], members.Select(member => member.GetProperty("exitCode").GetInt32()).ToArray());
        Assert.Equal([1, 2], members.Select(member => member.GetProperty("position").GetInt32()).ToArray());

        Assert.Equal(2, root.GetProperty("maximumReviewCorrectionAttempts").GetInt32());
        Assert.Equal(0, root.GetProperty("reviewCorrectionAttemptsUsed").GetInt32());
        Assert.False(root.GetProperty("correctionBudgetExhausted").GetBoolean());
        RequestVerificationDiagnosisEndpointTests.AssertNoDisclosure(body);
    }

    [Fact]
    public async Task Marks_the_findings_inapplicable_once_a_later_execution_changed_the_verification()
    {
        var seed = await VerificationDiagnosisApiSeed.SeedImplementedRunAsync(_factory);
        var failed = await VerificationDiagnosisApiSeed.SeedVerificationAsync(_factory, seed, 1, 1);
        await VerificationDiagnosisApiSeed.SeedCompletedDiagnosisAsync(_factory, seed, [failed], 1, attemptNumber: 4);
        await VerificationDiagnosisApiSeed.AddLaterExecutionAsync(_factory, seed, failed.CommandId, exitCode: 0);
        using var client = CreateAuthenticatedClient();

        var body = await client.GetStringAsync($"/api/runs/{seed.RunId}/agent-attempts/verification-diagnosis");

        using var document = JsonDocument.Parse(body);
        Assert.False(document.RootElement.GetProperty("correctionApplicable").GetBoolean());
    }
}
