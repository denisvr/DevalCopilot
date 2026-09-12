using System.Net.Http.Headers;
using System.Net.Http.Json;
using DevalCopilot.Api.Features.Projects.GetProjectRunSummaries;
using DevalCopilot.Api.Features.Runs.GetRunCockpit;
using DevalCopilot.Api.Features.Runs.GetRunEvents;
using DevalCopilot.Api.Features.Runs.StartSimulatedRun;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

public sealed class SimulatedRunFlowTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
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

    [Fact]
    public async Task Starting_a_simulated_run_reaches_a_visible_terminal_state_through_the_deterministic_sequence()
    {
        using var client = CreateAuthenticatedClient();

        var summaries = await client.GetFromJsonAsync<List<ProjectRunSummaryResponse>>("/api/projects/run-summaries");
        var project = Assert.Single(summaries!);

        var startResponse = await client.PostAsJsonAsync(
            "/api/runs/simulated", new StartSimulatedRunRequest(project.ProjectId, "Prove the walking skeleton"));
        startResponse.EnsureSuccessStatusCode();
        var started = await startResponse.Content.ReadFromJsonAsync<StartSimulatedRunResponse>();

        var cockpit = await WaitForTerminalCockpitAsync(client, started!.RunId);

        Assert.Equal("Completed", cockpit.Lifecycle);
        Assert.Equal("Completed", cockpit.Stage);
        Assert.Equal("None", cockpit.ActiveParticipant);

        // Only events for THIS run: Sequence is a global monotonic counter shared by every
        // run in the database, so it is not expected to equal the event count once more
        // than one run exists.
        var events = await client.GetFromJsonAsync<List<RunEventResponse>>($"/api/runs/{started.RunId}/events?after=0");
        Assert.NotNull(events);

        Assert.Equal(ExpectedEventTypesInOrder, events.Select(runEvent => runEvent.EventType));
        Assert.Equal(events.Select(e => e.Sequence).OrderBy(s => s), events.Select(e => e.Sequence));
        Assert.Equal(events[^1].Sequence, cockpit.LatestSequence);
    }

    [Fact]
    public async Task Querying_events_after_the_latest_sequence_returns_no_duplicates()
    {
        using var client = CreateAuthenticatedClient();

        var summaries = await client.GetFromJsonAsync<List<ProjectRunSummaryResponse>>("/api/projects/run-summaries");
        var project = Assert.Single(summaries!);

        var startResponse = await client.PostAsJsonAsync(
            "/api/runs/simulated", new StartSimulatedRunRequest(project.ProjectId, "Prove cursor catch-up"));
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
