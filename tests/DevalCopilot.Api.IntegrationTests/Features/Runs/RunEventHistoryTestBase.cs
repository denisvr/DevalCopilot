using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>Shared seeding and reading for the ADR-0034 event-history route tests. Rows are seeded through the Domain factories
/// (an event is not authority); only deliberately unrecognized stored values and sequences are written as SQL.</summary>
public abstract class RunEventHistoryTestBase(ApiWebApplicationFactory factory)
{
    protected ApiWebApplicationFactory Factory { get; } = factory;

    protected HttpClient AuthenticatedClient()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return client;
    }

    protected static string Route(Guid runId, string? query = null) =>
        $"/api/runs/{runId}/event-history{(query is null ? string.Empty : "?" + query)}";

    protected async Task<T> WithDbAsync<T>(Func<DevalCopilotDbContext, Task<T>> action)
    {
        using var scope = Factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>());
    }

    protected Task<Guid> SeedRunAsync() => WithDbAsync(async db =>
    {
        var project = Project.Register(Guid.NewGuid(), "Event history", $@"C:\repos\{Guid.NewGuid():N}", DateTimeOffset.UtcNow);
        db.Projects.Add(project);
        var run = Run.RecordClassifiedIntent(
            Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), RunExecutionMode.ManualAgent, "Objective", DateTimeOffset.UtcNow);
        db.Runs.Add(run);
        await db.SaveChangesAsync();
        return run.Id;
    });

    /// <summary>Appends <paramref name="count"/> events through the Domain factory, one save per event, and returns them in order.</summary>
    protected Task<List<RunEvent>> AppendAsync(Guid runId, int count, string payload = "{}") => WithDbAsync(async db =>
    {
        var events = new List<RunEvent>();
        for (var index = 0; index < count; index++)
        {
            var runEvent = RunEvent.Record(
                Guid.NewGuid(), runId, null, RunEventType.CollaborationMessageRecorded, ParticipantIdentity.ForOrchestrator(), payload,
                new DateTimeOffset(2026, 10, 9, 10, 0, 0, TimeSpan.Zero).AddSeconds(index));
            db.Events.Add(runEvent);
            await db.SaveChangesAsync();
            events.Add(runEvent);
        }

        return events;
    });

    protected Task ExecuteAsync(FormattableString sql) => WithDbAsync(async db => await db.Database.ExecuteSqlInterpolatedAsync(sql));

    protected async Task<(HttpStatusCode Status, JsonElement Body, string Text)> ReadAsync(Guid runId, string? query = null)
    {
        using var client = AuthenticatedClient();
        var response = await client.GetAsync(Route(runId, query));
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone(), text);
    }

    protected static string[] Sequences(JsonElement page) =>
        page.GetProperty("entries").EnumerateArray().Select(entry => entry.GetProperty("sequence").GetString()!).ToArray();
}
