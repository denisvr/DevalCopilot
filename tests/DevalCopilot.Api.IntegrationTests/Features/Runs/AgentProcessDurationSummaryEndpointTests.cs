using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>
/// Exercises the run-wide, read-only HOST-MEASURED Agent process-duration summary exposed by the
/// run cockpit through the real MVC pipeline: pure telemetry, never a budget, with an explicit
/// evidence-state classification that never conflates missing/malformed evidence with a real
/// zero-duration measurement, and that is scoped strictly to the requested run.
/// </summary>
public sealed class AgentProcessDurationSummaryEndpointTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>
{
    private static readonly string Fingerprint = new('a', 64);

    private HttpClient CreateAuthenticatedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return client;
    }

    private async Task<JsonElement> GetAgentProcessDurationSummaryAsync(Guid runId)
    {
        using var client = CreateAuthenticatedClient();
        var response = await client.GetAsync($"/api/runs/{runId}/cockpit");
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("agentProcessDurationSummary").Clone();
    }

    private async Task<Guid> SeedRunAsync(string projectName)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;
        var project = Project.Register(Guid.NewGuid(), projectName, $@"C:\repos\{Guid.NewGuid():N}", now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Expose the process-duration summary", now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        await dbContext.SaveChangesAsync();
        return run.Id;
    }

    private static Attempt ClaimAndDispatch(Guid runId, int attemptNumber, DateTimeOffset now)
    {
        var attempt = Attempt.ClaimAgent(
            Guid.NewGuid(), runId, attemptNumber, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 65536, 131072, now, attemptNumber);
        attempt.MarkAgentDispatched(now);
        return attempt;
    }

    /// <summary>Dispatched, terminal, with valid host-measured process-duration evidence.</summary>
    private async Task AddCompletedAttemptWithEvidenceAsync(Guid runId, int attemptNumber, TimeSpan duration)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;
        var attempt = ClaimAndDispatch(runId, attemptNumber, now);
        var evidence = AgentProcessExecutionEvidence.Create(ProcessOutcome.Exited, 0, duration);
        attempt.CompleteAgent(AgentOutcome.Proposed, Fingerprint, now.AddSeconds(1), evidence);
        dbContext.Attempts.Add(attempt);
        dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, Guid.NewGuid(), 0));
        await dbContext.SaveChangesAsync();
    }

    /// <summary>Dispatched, terminal, but with no process evidence recorded — the truthful
    /// "missing/malformed evidence" case, never silently zero.</summary>
    private async Task AddCompletedAttemptWithoutEvidenceAsync(Guid runId, int attemptNumber)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;
        var attempt = ClaimAndDispatch(runId, attemptNumber, now);
        attempt.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, now.AddSeconds(1), processEvidence: null);
        dbContext.Attempts.Add(attempt);
        dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, Guid.NewGuid(), 0));
        await dbContext.SaveChangesAsync();
    }

    /// <summary>Dispatched but still running — no terminal result yet.</summary>
    private async Task AddPendingDispatchedAttemptAsync(Guid runId, int attemptNumber)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;
        var attempt = ClaimAndDispatch(runId, attemptNumber, now);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task A_run_without_any_dispatched_agent_attempt_reports_no_dispatched_attempts()
    {
        var runId = await SeedRunAsync("No dispatched attempts");

        var summary = await GetAgentProcessDurationSummaryAsync(runId);

        Assert.Equal("NoDispatchedAttempts", summary.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, summary.GetProperty("totalMeasuredMilliseconds").ValueKind);
        Assert.Equal(0, summary.GetProperty("dispatchedAttemptCount").GetInt32());
    }

    [Fact]
    public async Task A_run_with_only_valid_terminal_evidence_reports_complete_with_the_exact_measured_total()
    {
        var runId = await SeedRunAsync("Complete evidence");
        await AddCompletedAttemptWithEvidenceAsync(runId, 1, TimeSpan.FromSeconds(10));
        await AddCompletedAttemptWithEvidenceAsync(runId, 2, TimeSpan.FromSeconds(5));

        var summary = await GetAgentProcessDurationSummaryAsync(runId);

        Assert.Equal("Complete", summary.GetProperty("status").GetString());
        Assert.Equal(15_000d, summary.GetProperty("totalMeasuredMilliseconds").GetDouble());
        Assert.Equal(2, summary.GetProperty("dispatchedAttemptCount").GetInt32());
        Assert.Equal(2, summary.GetProperty("validEvidenceCount").GetInt32());
        Assert.Equal(0, summary.GetProperty("pendingAttemptCount").GetInt32());
        Assert.Equal(0, summary.GetProperty("malformedEvidenceCount").GetInt32());
    }

    // A genuinely positive sub-millisecond total must never serialize as exactly zero — a truncating
    // integer-milliseconds projection would make it indistinguishable from "nothing measured".
    [Fact]
    public async Task A_positive_sub_millisecond_total_never_serializes_as_zero()
    {
        var runId = await SeedRunAsync("Sub-millisecond total");
        await AddCompletedAttemptWithEvidenceAsync(runId, 1, TimeSpan.FromTicks(500));

        var summary = await GetAgentProcessDurationSummaryAsync(runId);

        Assert.Equal("Complete", summary.GetProperty("status").GetString());
        var totalMilliseconds = summary.GetProperty("totalMeasuredMilliseconds").GetDouble();
        Assert.True(totalMilliseconds > 0, $"Expected a positive total, got {totalMilliseconds}.");
        Assert.Equal(0.05, totalMilliseconds);
    }

    // A real TimeSpan.Zero measurement must still serialize as an explicit, real zero — distinct
    // from a positive sub-millisecond total and from "no measurement available" (null).
    [Fact]
    public async Task A_genuine_zero_duration_total_serializes_as_a_real_zero_not_null()
    {
        var runId = await SeedRunAsync("Zero duration total");
        await AddCompletedAttemptWithEvidenceAsync(runId, 1, TimeSpan.Zero);

        var summary = await GetAgentProcessDurationSummaryAsync(runId);

        Assert.Equal("Complete", summary.GetProperty("status").GetString());
        var totalProperty = summary.GetProperty("totalMeasuredMilliseconds");
        Assert.NotEqual(JsonValueKind.Null, totalProperty.ValueKind);
        Assert.Equal(0d, totalProperty.GetDouble());
    }

    // An exactly-1ms total must round-trip precisely, never truncated or rounded away.
    [Fact]
    public async Task An_exact_one_millisecond_total_serializes_precisely()
    {
        var runId = await SeedRunAsync("One millisecond total");
        await AddCompletedAttemptWithEvidenceAsync(runId, 1, TimeSpan.FromMilliseconds(1));

        var summary = await GetAgentProcessDurationSummaryAsync(runId);

        Assert.Equal("Complete", summary.GetProperty("status").GetString());
        Assert.Equal(1d, summary.GetProperty("totalMeasuredMilliseconds").GetDouble());
    }

    // Every individual duration is valid (no malformed evidence at all), but their exact sum
    // overflows what a TimeSpan can represent — the distinct UnrepresentableTotal state, never
    // misreported as MalformedEvidence.
    [Fact]
    public async Task A_run_whose_valid_durations_overflow_when_summed_reports_unrepresentable_total()
    {
        var runId = await SeedRunAsync("Unrepresentable total");
        var huge = TimeSpan.FromTicks(long.MaxValue / 2 + 1);
        await AddCompletedAttemptWithEvidenceAsync(runId, 1, huge);
        await AddCompletedAttemptWithEvidenceAsync(runId, 2, huge);

        var summary = await GetAgentProcessDurationSummaryAsync(runId);

        Assert.Equal("UnrepresentableTotal", summary.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, summary.GetProperty("totalMeasuredMilliseconds").ValueKind);
        Assert.Equal(2, summary.GetProperty("validEvidenceCount").GetInt32());
        Assert.Equal(0, summary.GetProperty("malformedEvidenceCount").GetInt32());
    }

    [Fact]
    public async Task A_run_mixing_valid_evidence_missing_evidence_and_a_pending_attempt_reports_partial_evidence_with_no_total()
    {
        var runId = await SeedRunAsync("Mixed evidence");
        await AddCompletedAttemptWithEvidenceAsync(runId, 1, TimeSpan.FromSeconds(2));
        await AddCompletedAttemptWithoutEvidenceAsync(runId, 2);
        await AddPendingDispatchedAttemptAsync(runId, 3);

        var summary = await GetAgentProcessDurationSummaryAsync(runId);

        Assert.Equal("PartialEvidence", summary.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, summary.GetProperty("totalMeasuredMilliseconds").ValueKind);
        Assert.Equal(3, summary.GetProperty("dispatchedAttemptCount").GetInt32());
        Assert.Equal(1, summary.GetProperty("validEvidenceCount").GetInt32());
        Assert.Equal(1, summary.GetProperty("malformedEvidenceCount").GetInt32());
        Assert.Equal(1, summary.GetProperty("pendingAttemptCount").GetInt32());
    }

    [Fact]
    public async Task A_run_with_only_missing_terminal_evidence_reports_malformed_evidence_never_zero()
    {
        var runId = await SeedRunAsync("Malformed evidence only");
        await AddCompletedAttemptWithoutEvidenceAsync(runId, 1);

        var summary = await GetAgentProcessDurationSummaryAsync(runId);

        Assert.Equal("MalformedEvidence", summary.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, summary.GetProperty("totalMeasuredMilliseconds").ValueKind);
        Assert.Equal(0, summary.GetProperty("validEvidenceCount").GetInt32());
        Assert.Equal(1, summary.GetProperty("malformedEvidenceCount").GetInt32());
    }

    [Fact]
    public async Task A_second_runs_dispatched_attempts_never_leak_into_the_first_runs_summary()
    {
        var firstRunId = await SeedRunAsync("Isolation first run");
        var secondRunId = await SeedRunAsync("Isolation second run");

        await AddCompletedAttemptWithEvidenceAsync(firstRunId, 1, TimeSpan.FromSeconds(1));
        await AddCompletedAttemptWithEvidenceAsync(secondRunId, 1, TimeSpan.FromSeconds(9));
        await AddCompletedAttemptWithoutEvidenceAsync(secondRunId, 2);

        var firstSummary = await GetAgentProcessDurationSummaryAsync(firstRunId);
        var secondSummary = await GetAgentProcessDurationSummaryAsync(secondRunId);

        Assert.Equal("Complete", firstSummary.GetProperty("status").GetString());
        Assert.Equal(1_000d, firstSummary.GetProperty("totalMeasuredMilliseconds").GetDouble());
        Assert.Equal(1, firstSummary.GetProperty("dispatchedAttemptCount").GetInt32());

        Assert.Equal("PartialEvidence", secondSummary.GetProperty("status").GetString());
        Assert.Equal(2, secondSummary.GetProperty("dispatchedAttemptCount").GetInt32());
        Assert.Equal(1, secondSummary.GetProperty("validEvidenceCount").GetInt32());
        Assert.Equal(1, secondSummary.GetProperty("malformedEvidenceCount").GetInt32());
    }
}
