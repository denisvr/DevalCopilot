using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DevalCopilot.Api.Features.Runs.AbandonManualRun;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.RunAbandonmentInspection;

/// <summary>One real host (hosted services removed by the shared factory) over its own disposable migrated SQLite database, so a test
/// that writes damaged storage cannot disturb another. Runs are closed through the real protected abandon POST, so every Recorded case
/// starts from what the producer wrote. Helpers answer a read as its status, parsed body and raw text and fingerprint the stored run and
/// event rows.</summary>
public abstract class RunAbandonmentInspectionApiTestBase : IDisposable
{
    protected const string Reason = "The objective was replaced by a smaller change.";

    private readonly ApiWebApplicationFactory host = new();

    protected WebApplicationFactory<Program> Host => host;

    public void Dispose()
    {
        host.Dispose();
        GC.SuppressFinalize(this);
    }

    protected static string Route(Guid runId, string query = "") => $"/api/runs/{runId}/abandonment-inspection{query}";

    protected HttpClient AuthenticatedClient()
    {
        var client = Host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return client;
    }

    protected async Task<T> WithDbAsync<T>(Func<DevalCopilotDbContext, Task<T>> action)
    {
        using var scope = Host.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>());
    }

    /// <summary>A manual run of its own project, Created or Running, with nothing active.</summary>
    protected Task<(Guid ProjectId, Guid RunId)> SeedRunAsync(bool running = false, Guid? existingProject = null)
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-10);
        return WithDbAsync(async db =>
        {
            var project = existingProject is { } id
                ? await db.Projects.SingleAsync(candidate => candidate.Id == id)
                : Project.Register(Guid.NewGuid(), "Abandonment", $@"C:\repos\{Guid.NewGuid():N}", now);
            var run = Run.RecordClassifiedIntent(
                Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), RunExecutionMode.ManualAgent, "Plan the increment", now);
            if (running)
            {
                run.Claim(now.AddSeconds(5));
            }

            if (existingProject is null)
            {
                db.Projects.Add(project);
            }

            db.Runs.Add(run);
            db.Events.Add(RunEvent.Record(
                Guid.NewGuid(), run.Id, null, RunEventType.RunStarted, ParticipantIdentity.ForOrchestrator(), "{\"objective\":\"x\"}", now));
            await db.SaveChangesAsync();
            return (project.Id, run.Id);
        });
    }

    /// <summary>A run closed by the real protected abandon POST: the producer writes the reason, time and the one Human event.</summary>
    protected async Task<(Guid ProjectId, Guid RunId)> SeedAbandonedAsync(bool running = true, string reason = Reason)
    {
        var seeded = await SeedRunAsync(running);
        using var client = AuthenticatedClient();
        var response = await client.PostAsJsonAsync($"/api/runs/{seeded.RunId}/abandon", new AbandonManualRunRequest { Reason = reason });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return seeded;
    }

    protected async Task ExecuteAsync(FormattableString sql) => await WithDbAsync(async db =>
    {
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");
        return await db.Database.ExecuteSqlInterpolatedAsync(sql);
    });

    protected async Task<(HttpStatusCode Status, JsonElement Body, string Text)> GetAsync(string route)
    {
        using var client = AuthenticatedClient();
        using var response = await client.GetAsync(route);
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone(), text);
    }

    protected static string[] Names(JsonElement element) => element.EnumerateObject().Select(property => property.Name).ToArray();

    protected static string Time(DateTimeOffset value) => value.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Every stored run and event row the read touches and the counts of every table near it, as one comparable value.</summary>
    protected Task<string> FingerprintAsync() => WithDbAsync(async db =>
    {
        var runs = await db.Database.SqlQueryRaw<string>(
            "SELECT Id || '|' || Lifecycle || '|' || Stage || '|' || ifnull(AbandonmentReason, '~') || '|' || ifnull(CAST(AbandonedAtUtc AS TEXT), '~') "
            + "|| '|' || LastAdvancedAtUtc AS Value FROM runs ORDER BY Id").ToListAsync();
        var events = await db.Database.SqlQueryRaw<string>(
            "SELECT Sequence || '|' || Id || '|' || RunId || '|' || EventType || '|' || ActorKind || '|' || PayloadJson || '|' || OccurredAtUtc "
            + "AS Value FROM events ORDER BY Sequence").ToListAsync();
        return string.Join(";", runs) + "#" + string.Join(";", events) + "#" + await db.Projects.CountAsync() + "#"
            + await db.GitWorkspaces.CountAsync() + "#" + await db.Attempts.CountAsync() + "#" + await db.Artifacts.CountAsync();
    });
}
