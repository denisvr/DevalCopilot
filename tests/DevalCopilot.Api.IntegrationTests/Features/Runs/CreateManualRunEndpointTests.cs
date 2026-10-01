using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DevalCopilot.Api.Features.Projects.GetProjectRunSummaries;
using DevalCopilot.Api.Features.Runs.CreateManualRun;
using DevalCopilot.Api.Features.Runs.GetRunCockpit;
using DevalCopilot.Api.Features.Runs.StartSimulatedRun;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>The protected manual creation operation and the execution-mode projections, against the real host with no
/// hosted supervisor running.</summary>
public sealed class CreateManualRunEndpointTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    private HttpClient CreateAuthenticatedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return client;
    }

    private async Task<Guid> RegisterProjectAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var project = Project.Register(Guid.NewGuid(), "Manual intake", $@"C:\repos\{Guid.NewGuid():N}", DateTimeOffset.UtcNow);
        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync();
        return project.Id;
    }

    private async Task<T> WithDbAsync<T>(Func<DevalCopilotDbContext, Task<T>> action)
    {
        using var scope = factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>());
    }

    [Fact]
    public async Task Creation_requires_authentication()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/runs/manual", new CreateManualRunRequest(Guid.NewGuid(), "Objective"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Creation_records_a_manual_run_with_its_number_and_the_default_budgets()
    {
        using var client = CreateAuthenticatedClient();
        var projectId = await RegisterProjectAsync();

        var response = await client.PostAsJsonAsync("/api/runs/manual", new CreateManualRunRequest(projectId, "Plan the increment"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<CreateManualRunResponse>())!;
        Assert.Equal(1, created.ExecutionNumber);

        var cockpit = (await client.GetFromJsonAsync<GetRunCockpitResponse>($"/api/runs/{created.RunId}/cockpit"))!;
        Assert.Equal("ManualAgent", cockpit.ExecutionMode);
        Assert.Equal("Created", cockpit.Lifecycle);
        Assert.Equal("Intake", cockpit.Stage);
        Assert.Equal("Plan the increment", cockpit.Objective);
        Assert.Equal(16, cockpit.MaximumAgentAttempts);
        Assert.Equal(0, cockpit.AgentAttemptsUsed);
        Assert.False(cockpit.AgentInvocationTimeBudget.IsLegacyUnknown);
        Assert.Equal((long)TimeSpan.FromMinutes(120).TotalMilliseconds, cockpit.AgentInvocationTimeBudget.RemainingMilliseconds);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_objective_is_a_validation_failure_that_creates_nothing(string objective)
    {
        using var client = CreateAuthenticatedClient();
        var projectId = await RegisterProjectAsync();

        var response = await client.PostAsJsonAsync("/api/runs/manual", new CreateManualRunRequest(projectId, objective));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await WithDbAsync(db => db.Runs.CountAsync(run => run.ProjectId == projectId)));
    }

    [Fact]
    public async Task An_objective_over_two_thousand_characters_is_refused_and_exactly_two_thousand_is_accepted()
    {
        using var client = CreateAuthenticatedClient();
        var projectId = await RegisterProjectAsync();

        var tooLong = await client.PostAsJsonAsync("/api/runs/manual", new CreateManualRunRequest(projectId, new string('x', 2001)));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(0, await WithDbAsync(db => db.Runs.CountAsync(run => run.ProjectId == projectId)));

        var limit = await client.PostAsJsonAsync("/api/runs/manual", new CreateManualRunRequest(projectId, new string('x', 2000)));
        Assert.Equal(HttpStatusCode.OK, limit.StatusCode);
    }

    [Fact]
    public async Task A_missing_project_is_a_safe_not_found()
    {
        using var client = CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/runs/manual", new CreateManualRunRequest(Guid.NewGuid(), "Objective"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("projects.not_found", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unfinished_run_blocks_both_creation_operations_with_a_safe_conflict_and_changes_nothing()
    {
        using var client = CreateAuthenticatedClient();
        var projectId = await RegisterProjectAsync();
        (await client.PostAsJsonAsync("/api/runs/manual", new CreateManualRunRequest(projectId, "First"))).EnsureSuccessStatusCode();
        var before = await WithDbAsync(async db => (
            await db.Runs.CountAsync(run => run.ProjectId == projectId),
            (await db.Projects.SingleAsync(project => project.Id == projectId)).NextExecutionNumber));

        var manual = await client.PostAsJsonAsync("/api/runs/manual", new CreateManualRunRequest(projectId, "Second"));
        var simulated = await client.PostAsJsonAsync("/api/runs/simulated", new StartSimulatedRunRequest(projectId, "Demo"));

        foreach (var response in new[] { manual, simulated })
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("runs.intent_blocked", body, StringComparison.Ordinal);
            // The frontend reads the refusal code from errors[0].code of the Problem Details body.
            using var problem = System.Text.Json.JsonDocument.Parse(body);
            Assert.Equal("runs.intent_blocked", problem.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
            Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
        }

        var after = await WithDbAsync(async db => (
            await db.Runs.CountAsync(run => run.ProjectId == projectId),
            (await db.Projects.SingleAsync(project => project.Id == projectId)).NextExecutionNumber));
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Terminal_history_permits_creation_and_the_summary_hint_follows_all_runs()
    {
        using var client = CreateAuthenticatedClient();
        var projectId = await RegisterProjectAsync();

        async Task<ProjectRunSummaryResponse> SummaryAsync() =>
            (await client.GetFromJsonAsync<List<ProjectRunSummaryResponse>>("/api/projects/run-summaries"))!
            .Single(summary => summary.ProjectId == projectId);

        var empty = await SummaryAsync();
        Assert.True(empty.CanCreateRun);
        Assert.Null(empty.ExecutionMode);

        var created = (await (await client.PostAsJsonAsync("/api/runs/manual", new CreateManualRunRequest(projectId, "First")))
            .Content.ReadFromJsonAsync<CreateManualRunResponse>())!;
        var active = await SummaryAsync();
        Assert.False(active.CanCreateRun);
        Assert.Equal("ManualAgent", active.ExecutionMode);
        Assert.Equal(created.RunId, active.RunId);

        await WithDbAsync(async db =>
        {
            var run = await db.Runs.SingleAsync(candidate => candidate.Id == created.RunId);
            run.Claim(DateTimeOffset.UtcNow);
            run.Fail(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
            return 0;
        });

        var terminal = await SummaryAsync();
        Assert.True(terminal.CanCreateRun);
        var second = await client.PostAsJsonAsync("/api/runs/manual", new CreateManualRunRequest(projectId, "Second"));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(2, (await second.Content.ReadFromJsonAsync<CreateManualRunResponse>())!.ExecutionNumber);
    }

    [Theory]
    [InlineData(0, "Legacy")]
    [InlineData(1, "Simulated")]
    [InlineData(2, "ManualAgent")]
    [InlineData(9, "Unrecognized")]
    public async Task Summaries_and_the_cockpit_report_the_stored_mode_and_never_promote_an_undefined_number(int stored, string expected)
    {
        using var client = CreateAuthenticatedClient();
        var projectId = await RegisterProjectAsync();
        var runId = await WithDbAsync(async db =>
        {
            var project = await db.Projects.SingleAsync(candidate => candidate.Id == projectId);
            var run = Run.RecordIntent(Guid.NewGuid(), projectId, project.ReserveExecutionNumber(), "Stored mode", DateTimeOffset.UtcNow);
            db.Runs.Add(run);
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE runs SET ExecutionMode = {stored} WHERE Id = {run.Id}");
            return run.Id;
        });

        var summary = (await client.GetFromJsonAsync<List<ProjectRunSummaryResponse>>("/api/projects/run-summaries"))!
            .Single(candidate => candidate.ProjectId == projectId);
        var cockpit = (await client.GetFromJsonAsync<GetRunCockpitResponse>($"/api/runs/{runId}/cockpit"))!;

        Assert.Equal(expected, summary.ExecutionMode);
        Assert.Equal(expected, cockpit.ExecutionMode);
        Assert.False(summary.CanCreateRun);
    }

    public static TheoryData<string> MalformedStoredModes() =>
        ["2.5", "1.0000001", "4294967296", "4294967298", "9223372036854775807", "-1", "'two'", "''", "X'02'", "X''"];

    [Theory]
    [MemberData(nameof(MalformedStoredModes))]
    public async Task A_malformed_stored_mode_is_reported_as_unrecognized_without_failing_the_project_list_or_cockpit(string literal)
    {
        using var client = CreateAuthenticatedClient();
        var healthyProjectId = await RegisterProjectAsync();
        (await client.PostAsJsonAsync("/api/runs/manual", new CreateManualRunRequest(healthyProjectId, "Healthy sibling"))).EnsureSuccessStatusCode();
        var projectId = await RegisterProjectAsync();
        var runId = await WithDbAsync(async db =>
        {
            var run = Run.RecordIntent(Guid.NewGuid(), projectId, 1, "Malformed stored mode", DateTimeOffset.UtcNow);
            db.Runs.Add(run);
            await db.SaveChangesAsync();
#pragma warning disable EF1003
            await db.Database.ExecuteSqlRawAsync("UPDATE runs SET ExecutionMode = " + literal + " WHERE Id = {0}", run.Id);
#pragma warning restore EF1003
            return run.Id;
        });

        var summaries = (await client.GetFromJsonAsync<List<ProjectRunSummaryResponse>>("/api/projects/run-summaries"))!;
        var cockpit = (await client.GetFromJsonAsync<GetRunCockpitResponse>($"/api/runs/{runId}/cockpit"))!;

        Assert.Equal("Unrecognized", summaries.Single(candidate => candidate.ProjectId == projectId).ExecutionMode);
        Assert.False(summaries.Single(candidate => candidate.ProjectId == projectId).CanCreateRun);
        Assert.Equal("Unrecognized", cockpit.ExecutionMode);
        Assert.Equal("ManualAgent", summaries.Single(candidate => candidate.ProjectId == healthyProjectId).ExecutionMode);
    }

    [Fact]
    public async Task An_unrecognized_lifecycle_keeps_that_project_blocked_while_the_list_and_healthy_projects_stay_usable()
    {
        using var client = CreateAuthenticatedClient();
        var blockedProjectId = await RegisterProjectAsync();
        var emptyProjectId = await RegisterProjectAsync();
        var historyProjectId = await RegisterProjectAsync();
        await WithDbAsync(async db =>
        {
            var blockedProject = await db.Projects.SingleAsync(candidate => candidate.Id == blockedProjectId);
            var archived = Run.RecordIntent(Guid.NewGuid(), blockedProjectId, blockedProject.ReserveExecutionNumber(), "Archived by a newer build", DateTimeOffset.UtcNow);
            archived.Claim(DateTimeOffset.UtcNow);
            archived.Complete(DateTimeOffset.UtcNow);
            db.Runs.Add(archived);
            var historyProject = await db.Projects.SingleAsync(candidate => candidate.Id == historyProjectId);
            var completed = Run.RecordIntent(Guid.NewGuid(), historyProjectId, historyProject.ReserveExecutionNumber(), "Completed history", DateTimeOffset.UtcNow);
            completed.Claim(DateTimeOffset.UtcNow);
            completed.Complete(DateTimeOffset.UtcNow);
            db.Runs.Add(completed);
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE runs SET Lifecycle = 'Archived' WHERE ProjectId = {blockedProjectId}");
            return 0;
        });
        var before = await WithDbAsync(async db => (
            await db.Runs.CountAsync(run => run.ProjectId == blockedProjectId),
            await db.Events.CountAsync(runEvent => db.Runs.Any(run => run.Id == runEvent.RunId && run.ProjectId == blockedProjectId)),
            (await db.Projects.SingleAsync(project => project.Id == blockedProjectId)).NextExecutionNumber));

        var listResponse = await client.GetAsync("/api/projects/run-summaries");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var body = await listResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Archived", body, StringComparison.Ordinal);
        var summaries = System.Text.Json.JsonSerializer.Deserialize<List<ProjectRunSummaryResponse>>(
            body, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;

        var blocked = summaries.Single(candidate => candidate.ProjectId == blockedProjectId);
        Assert.False(blocked.CanCreateRun);
        Assert.Null(blocked.Lifecycle);
        Assert.True(summaries.Single(candidate => candidate.ProjectId == emptyProjectId).CanCreateRun);
        Assert.True(summaries.Single(candidate => candidate.ProjectId == historyProjectId).CanCreateRun);

        foreach (var response in new[]
                 {
                     await client.PostAsJsonAsync("/api/runs/manual", new CreateManualRunRequest(blockedProjectId, "Refused")),
                     await client.PostAsJsonAsync("/api/runs/simulated", new StartSimulatedRunRequest(blockedProjectId, "Refused")),
                 })
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains("runs.intent_blocked", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        var after = await WithDbAsync(async db => (
            await db.Runs.CountAsync(run => run.ProjectId == blockedProjectId),
            await db.Events.CountAsync(runEvent => db.Runs.Any(run => run.Id == runEvent.RunId && run.ProjectId == blockedProjectId)),
            (await db.Projects.SingleAsync(project => project.Id == blockedProjectId)).NextExecutionNumber));
        Assert.Equal(before, after);

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/runs/manual", new CreateManualRunRequest(emptyProjectId, "Healthy"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/runs/manual", new CreateManualRunRequest(historyProjectId, "After history"))).StatusCode);
    }
}
