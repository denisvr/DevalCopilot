using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>The direct-human-guidance fact across the implementation status, the review-correction status, the cockpit
/// and the historical attempt evidence, through the real HTTP pipeline: truthful absence, the exact accepted text, and
/// safe rendering of malformed or incompatible stored state (200, state Unknown, no text, no throw).</summary>
public sealed class DirectHumanGuidanceProjectionEndpointTests(CodexPlanningApiWebApplicationFactory factory)
    : IClassFixture<CodexPlanningApiWebApplicationFactory>
{
    private const string Guidance = "SENTINEL-PROJ-9 Reuse the existing helper.\nKeep it small.";

    public enum Path
    {
        Implementation,
        Correction,
    }

    public static IEnumerable<object[]> Paths => [[Path.Implementation], [Path.Correction]];

    private HttpClient CreateClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return client;
    }

    private async Task<(Guid RunId, Guid AttemptId)> SeedAsync(Path path, string? guidance, bool legacyV1 = false)
    {
        var now = DateTimeOffset.UtcNow;
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var project = Project.Register(Guid.NewGuid(), "Direct guidance", $@"C:\repos\{Guid.NewGuid():N}", now);
        var run = Run.RecordIntent(runId, project.Id, 1, "Objective", now);
        run.Claim(now);
        var attempt = (path, legacyV1) switch
        {
            (Path.Implementation, true) => Attempt.ClaimAgentImplementation(
                attemptId, runId, 1, Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), Guid.NewGuid(),
                TimeSpan.FromMinutes(20), 65536, 131072, now, 1),
            (Path.Implementation, false) => Attempt.ClaimAgentImplementationWithAssignment(
                attemptId, runId, 1, Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), Guid.NewGuid(),
                TimeSpan.FromMinutes(20), 65536, 131072, now, null, null, AgentPermissionProfile.WorkspaceEditOnly,
                ClaudeMutationAdapterContract.ImplementationV2, 1, null, guidance),
            (_, true) => Attempt.ClaimAgentReviewCorrection(
                attemptId, runId, 1, Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), Guid.NewGuid(),
                TimeSpan.FromMinutes(20), 65536, 131072, now, 1),
            _ => Attempt.ClaimAgentReviewCorrectionWithModelRequest(
                attemptId, runId, 1, Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), Guid.NewGuid(),
                TimeSpan.FromMinutes(20), 65536, 131072, now, null, null, 1, null, guidance),
        };
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), attemptId, Guid.NewGuid(), 0));
        await dbContext.SaveChangesAsync();
        return (runId, attemptId);
    }

    private async Task ExecuteSqlAsync(FormattableString sql)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        await dbContext.Database.ExecuteSqlInterpolatedAsync(sql);
    }

    private async Task<JsonElement> GetAsync(string url)
    {
        using var client = CreateClient();
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static string StatusUrl(Path path, Guid runId) => path == Path.Implementation
        ? $"/api/runs/{runId}/agent-attempts/implementation"
        : $"/api/runs/{runId}/agent-attempts/review-correction";

    private static void AssertFact(JsonElement fact, string state, string? text)
    {
        Assert.Equal(state, fact.GetProperty("state").GetString());
        if (text is null)
        {
            Assert.Equal(JsonValueKind.Null, fact.GetProperty("text").ValueKind);
        }
        else
        {
            Assert.Equal(text, fact.GetProperty("text").GetString());
        }
    }

    private async Task AssertAllProjectionsAsync(Path path, Guid runId, Guid attemptId, string state, string? text)
    {
        var status = await GetAsync(StatusUrl(path, runId));
        var cockpit = await GetAsync($"/api/runs/{runId}/cockpit");
        var evidence = await GetAsync($"/api/runs/{runId}/agent-attempts/{attemptId}/evidence");

        if (path == Path.Implementation)
        {
            // A bare correction attempt has no review lineage, so its status resolves no current attempt; the
            // correction status fact is proved with a full lineage in the request endpoint tests.
            AssertFact(status.GetProperty("directGuidance"), state, text);
        }

        AssertFact(cockpit.GetProperty("latestAgentAttempt").GetProperty("directGuidance"), state, text);
        AssertFact(evidence.GetProperty("directGuidance"), state, text);
        if (text is null)
        {
            Assert.DoesNotContain("SENTINEL", status.GetRawText() + cockpit.GetRawText() + evidence.GetRawText(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task A_guided_attempt_exposes_exactly_the_accepted_text_in_every_projection(Path path)
    {
        var (runId, attemptId) = await SeedAsync(path, Guidance);

        await AssertAllProjectionsAsync(path, runId, attemptId, "Provided", Guidance);
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task A_null_snapshot_is_not_recorded_whether_the_attempt_is_a_current_v2_or_a_legacy_v1_one(Path path)
    {
        var (runId, attemptId) = await SeedAsync(path, null);
        var (legacyRunId, legacyAttemptId) = await SeedAsync(path, null, legacyV1: true);

        await AssertAllProjectionsAsync(path, runId, attemptId, "NotRecorded", null);
        await AssertAllProjectionsAsync(path, legacyRunId, legacyAttemptId, "NotRecorded", null);
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task Malformed_stored_text_is_unknown_with_no_text_and_never_breaks_a_projection(Path path)
    {
        foreach (var stored in new[] { "", "   ", " SENTINEL-PROJ-9 padded ", "SENTINEL-PROJ-9 cafe\u0301", "SENTINEL-PROJ-9\r\nCRLF", new string('x', 601) + "SENTINEL-PROJ-9" })
        {
            var (runId, attemptId) = await SeedAsync(path, Guidance);
            await ExecuteSqlAsync($"UPDATE attempts SET AgentDirectHumanGuidance = {stored} WHERE Id = {attemptId}");

            await AssertAllProjectionsAsync(path, runId, attemptId, "Unknown", null);
        }
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task Guidance_stored_beside_a_v1_contract_is_unknown_and_discloses_no_text(Path path)
    {
        var (runId, attemptId) = await SeedAsync(path, null, legacyV1: true);
        await ExecuteSqlAsync($"UPDATE attempts SET AgentDirectHumanGuidance = {Guidance} WHERE Id = {attemptId}");

        await AssertAllProjectionsAsync(path, runId, attemptId, "Unknown", null);
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task With_no_attempt_the_status_has_no_guidance_fact(Path path)
    {
        var now = DateTimeOffset.UtcNow;
        var runId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var project = Project.Register(Guid.NewGuid(), "No attempt", $@"C:\repos\{Guid.NewGuid():N}", now);
            dbContext.Projects.Add(project);
            dbContext.Runs.Add(Run.RecordIntent(runId, project.Id, 1, "Objective", now));
            await dbContext.SaveChangesAsync();
        }

        var status = await GetAsync(StatusUrl(path, runId));

        Assert.False(status.GetProperty("hasAttempt").GetBoolean());
        Assert.Equal(JsonValueKind.Null, status.GetProperty("directGuidance").ValueKind);
    }

    [Fact]
    public async Task An_attempt_that_is_not_a_claude_mutation_attempt_carries_no_guidance_fact()
    {
        var now = DateTimeOffset.UtcNow;
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var project = Project.Register(Guid.NewGuid(), "Critical review", $@"C:\repos\{Guid.NewGuid():N}", now);
            var run = Run.RecordIntent(runId, project.Id, 1, "Objective", now);
            run.Claim(now);
            dbContext.Projects.Add(project);
            dbContext.Runs.Add(run);
            dbContext.Attempts.Add(Attempt.ClaimAgentCriticalReviewWithModelRequest(
                attemptId, runId, 1, Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 262144, 524288, now, null, null, agentBudgetSlot: 1));
            await dbContext.SaveChangesAsync();
        }

        var cockpit = await GetAsync($"/api/runs/{runId}/cockpit");
        var evidence = await GetAsync($"/api/runs/{runId}/agent-attempts/{attemptId}/evidence");

        Assert.Equal(JsonValueKind.Null, cockpit.GetProperty("latestAgentAttempt").GetProperty("directGuidance").ValueKind);
        Assert.Equal(JsonValueKind.Null, evidence.GetProperty("directGuidance").ValueKind);
    }
}
