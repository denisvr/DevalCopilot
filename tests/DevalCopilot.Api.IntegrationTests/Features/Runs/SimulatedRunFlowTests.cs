using System.Net.Http.Headers;
using System.Net.Http.Json;
using DevalCopilot.Api.Features.Runs.GetRunCockpit;
using DevalCopilot.Api.Features.Runs.GetCollaborationTimeline;
using DevalCopilot.Api.Features.Runs.GetRunEvents;
using DevalCopilot.Api.Features.Runs.StartSimulatedRun;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

public sealed class SimulatedRunFlowTests(SimulatedRunApiWebApplicationFactory factory)
    : IClassFixture<SimulatedRunApiWebApplicationFactory>
{
    private static readonly string[] ExpectedEventTypesInOrder =
    [
        "run.started",
        "codex.proposal",
        "claude.challenge",
        "codex.resolution",
        "claude.execution",
        "run.completed",
    ];

    private HttpClient CreateAuthenticatedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return client;
    }

    /// <summary>
    /// Registers a project directly through the DbContext, bypassing the real registration
    /// endpoint (and the real Git repository it requires) entirely — these tests are about the
    /// simulated-run sequence, not registration correctness, which is proven separately. Each
    /// call uses a fresh, uniquely named project rather than relying on any pre-seeded row, since
    /// production startup no longer seeds one and the class-shared factory's database persists
    /// across every test in this file.
    /// </summary>
    private async Task<Guid> RegisterProjectAsync(string name)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var project = Project.Register(Guid.NewGuid(), name, $@"C:\repos\{Guid.NewGuid():N}", DateTimeOffset.UtcNow);
        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync();
        return project.Id;
    }

    [Fact]
    public async Task Starting_a_simulated_run_reaches_a_visible_terminal_state_through_the_deterministic_sequence()
    {
        using var client = CreateAuthenticatedClient();

        var projectId = await RegisterProjectAsync("Walking skeleton");

        var startResponse = await client.PostAsJsonAsync(
            "/api/runs/simulated", new StartSimulatedRunRequest(projectId, "Prove the walking skeleton"));
        startResponse.EnsureSuccessStatusCode();
        var started = await startResponse.Content.ReadFromJsonAsync<StartSimulatedRunResponse>();

        var cockpit = await WaitForTerminalCockpitAsync(client, started!.RunId);

        Assert.Equal("Completed", cockpit.Lifecycle);
        Assert.Equal("Completed", cockpit.Stage);
        Assert.Equal("None", cockpit.ActiveParticipant.Kind);
        Assert.Null(cockpit.ActiveParticipant.Role);
        Assert.Null(cockpit.ActiveParticipant.Provider);

        // A run that never claimed any Agent attempt still safely reports the run-wide Agent
        // claim budget: its own default maximum, zero used, never exhausted.
        Assert.Equal(16, cockpit.MaximumAgentAttempts);
        Assert.Equal(0, cockpit.AgentAttemptsUsed);
        Assert.False(cockpit.AgentBudgetExhausted);

        // The independent run-wide Agent invocation-TIME budget: a newly created run is budgeted
        // (never the legacy/unknown state), has reserved nothing, and its remaining time equals
        // its own maximum — a bounded, truthful projection, never a fabricated "exhausted" flag.
        Assert.False(cockpit.AgentInvocationTimeBudget.IsLegacyUnknown);
        Assert.False(cockpit.AgentInvocationTimeBudget.EvidenceInvalid);
        Assert.Equal((long)TimeSpan.FromMinutes(120).TotalMilliseconds, cockpit.AgentInvocationTimeBudget.MaximumMilliseconds);
        Assert.Equal(0L, cockpit.AgentInvocationTimeBudget.ReservedMilliseconds);
        Assert.Equal((long)TimeSpan.FromMinutes(120).TotalMilliseconds, cockpit.AgentInvocationTimeBudget.RemainingMilliseconds);

        // Only events for THIS run: Sequence is a global monotonic counter shared by every
        // run in the database, so it is not expected to equal the event count once more
        // than one run exists.
        var events = await client.GetFromJsonAsync<List<RunEventResponse>>($"/api/runs/{started.RunId}/events?after=0");
        Assert.NotNull(events);

        Assert.Equal(ExpectedEventTypesInOrder, events.Select(runEvent => runEvent.EventType));
        Assert.Equal(events.Select(e => e.Sequence).OrderBy(s => s), events.Select(e => e.Sequence));
        Assert.Equal(events[^1].Sequence, cockpit.LatestSequence);

        var timeline = await client.GetFromJsonAsync<List<CollaborationMessageTimelineResponse>>(
            $"/api/runs/{started.RunId}/collaboration-timeline");
        Assert.NotNull(timeline);
        Assert.Equal(["Proposal", "Challenge", "Decision", "ExecutionReport"], timeline.Select(message => message.Type));
        Assert.All(timeline, message => Assert.Equal("Simulated", message.Provenance));
        Assert.Equal(timeline[1].Id, timeline[2].InReplyToMessageId);
    }

    [Fact]
    public async Task Cockpit_reports_the_legacy_unknown_time_budget_state_for_a_run_that_predates_the_policy()
    {
        using var client = CreateAuthenticatedClient();
        var projectId = await RegisterProjectAsync("Legacy time budget");

        var startResponse = await client.PostAsJsonAsync(
            "/api/runs/simulated", new StartSimulatedRunRequest(projectId, "Prove the legacy time-budget state"));
        var started = await startResponse.Content.ReadFromJsonAsync<StartSimulatedRunResponse>();
        await WaitForTerminalCockpitAsync(client, started!.RunId);

        // Reproduces a historical Run predating the time-budget decision — a direct write against
        // the persisted column, exactly as the additive migration itself would have left it.
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE runs SET MaximumAgentInvocationTime = NULL WHERE Id = {started.RunId}");
        }

        var cockpit = await client.GetFromJsonAsync<GetRunCockpitResponse>($"/api/runs/{started.RunId}/cockpit");

        Assert.True(cockpit!.AgentInvocationTimeBudget.IsLegacyUnknown);
        Assert.False(cockpit.AgentInvocationTimeBudget.EvidenceInvalid);
        Assert.Null(cockpit.AgentInvocationTimeBudget.MaximumMilliseconds);
        Assert.Null(cockpit.AgentInvocationTimeBudget.ReservedMilliseconds);
        Assert.Null(cockpit.AgentInvocationTimeBudget.RemainingMilliseconds);
    }

    [Fact]
    public async Task Querying_events_after_the_latest_sequence_returns_no_duplicates()
    {
        using var client = CreateAuthenticatedClient();

        var projectId = await RegisterProjectAsync("Cursor catch-up");

        var startResponse = await client.PostAsJsonAsync(
            "/api/runs/simulated", new StartSimulatedRunRequest(projectId, "Prove cursor catch-up"));
        var started = await startResponse.Content.ReadFromJsonAsync<StartSimulatedRunResponse>();

        var cockpit = await WaitForTerminalCockpitAsync(client, started!.RunId);

        var allEvents = await client.GetFromJsonAsync<List<RunEventResponse>>($"/api/runs/{started.RunId}/events?after=0");
        var afterLatest = await client.GetFromJsonAsync<List<RunEventResponse>>(
            $"/api/runs/{started.RunId}/events?after={cockpit.LatestSequence}");

        Assert.Equal(ExpectedEventTypesInOrder.Length, allEvents!.Count);
        Assert.Empty(afterLatest!);
    }

    private static async Task<GetRunCockpitResponse> WaitForTerminalCockpitAsync(HttpClient client, Guid runId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);

        while (DateTime.UtcNow < deadline)
        {
            var cockpit = await client.GetFromJsonAsync<GetRunCockpitResponse>($"/api/runs/{runId}/cockpit");
            if (cockpit!.Lifecycle == "Completed")
            {
                return cockpit;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"Run {runId} did not reach a terminal state in time.");
    }
}
