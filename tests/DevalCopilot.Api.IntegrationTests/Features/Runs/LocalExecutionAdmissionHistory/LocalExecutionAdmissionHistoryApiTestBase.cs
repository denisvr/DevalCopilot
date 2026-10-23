using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionSequences;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionAdmissionHistory;

/// <summary>The real protected host over a disposable file-backed database and the production queue, immediate-arming and stop operations
/// that create the cycles the admission-history reads are asserted against (ADR-0063). A class that changes storage owns its factory.</summary>
public abstract class LocalExecutionAdmissionHistoryApiTestBase
{
    protected LocalExecutionAdmissionHistoryApiTestBase(ApiWebApplicationFactory baseFactory)
    {
        Factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IGitWorkspaceEvidenceReader>();
            services.AddSingleton<IGitWorkspaceEvidenceReader>(Evidence);
        }));
    }

    private protected WebApplicationFactory<Program> Factory { get; }

    private protected LocalExecutionSequenceMatchingEvidenceReader Evidence { get; } = new();

    private protected HttpClient Client(bool authenticated = true)
    {
        var client = Factory.CreateClient();
        if (authenticated)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        }

        return client;
    }

    private protected async Task<T> WithDbAsync<T>(Func<DevalCopilotDbContext, Task<T>> action)
    {
        using var scope = Factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>());
    }

    private protected Task<LocalExecutionSequenceApiScene> SeedAsync() =>
        WithDbAsync(db => LocalExecutionSequenceApiSeed.SeedAsync(db, 2, RunExecutionMode.ManualAgent, true, null));

    private protected static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private protected static string Admissions(Guid run) => $"/api/runs/{run}/local-execution-sequence/inspection/admissions";

    private protected static string Body(LocalExecutionSequenceApiScene scene, string confirmation) =>
        JsonSerializer.Serialize(new
        {
            planProposalMessageId = scene.PlanId,
            workspaceId = scene.WorkspaceId,
            checkpointId = scene.CheckpointId,
            fingerprintSha256 = LocalExecutionSequenceApiSeed.Fingerprint,
            confirmationSha256 = confirmation,
        });

    private protected static string[] Names(JsonElement element) =>
        element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();

    private protected async Task<(HttpStatusCode Status, JsonElement Body, string Text)> GetAsync(string route)
    {
        using var client = Client();
        var response = await client.GetAsync(route);
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone(), text);
    }

    private protected async Task<(LocalExecutionSequenceApiScene Scene, Guid SequenceId)> QueuedAsync()
    {
        var scene = await SeedAsync();
        using var client = Client();
        var availability = await client.GetAsync(
            $"/api/runs/{scene.RunId}/local-execution-sequence/queue/availability?planProposalMessageId={scene.PlanId}");
        Assert.Equal(HttpStatusCode.OK, availability.StatusCode);
        var confirmation = JsonDocument.Parse(await availability.Content.ReadAsStringAsync()).RootElement
            .GetProperty("confirmationSha256").GetString()!;
        var queued = await client.PostAsync($"/api/runs/{scene.RunId}/local-execution-sequence/queue", Json(Body(scene, confirmation)));
        Assert.Equal(HttpStatusCode.OK, queued.StatusCode);
        var sequenceId = JsonDocument.Parse(await queued.Content.ReadAsStringAsync()).RootElement.GetProperty("sequenceId").GetGuid();
        return (scene, sequenceId);
    }

    private protected async Task<int> RecordedFactsAsync() => await WithDbAsync(async db =>
        await db.Events.AsNoTracking().CountAsync() + await db.LocalExecutionAdmissions.AsNoTracking().CountAsync()
        + await db.LocalExecutionSequenceSteps.AsNoTracking().CountAsync() + await db.Attempts.AsNoTracking().CountAsync()
        + await db.VerificationExecutions.AsNoTracking().CountAsync());

    private protected async Task CorruptAsync(string sql)
    {
        var dataSource = await WithDbAsync(db => Task.FromResult(db.Database.GetDbConnection().DataSource));
        await using var connection = new SqliteConnection($"Data Source={dataSource};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // Test-owned statement text without any caller-supplied value.
        command.CommandText = "PRAGMA ignore_check_constraints = ON; PRAGMA foreign_keys = OFF; " + sql;
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync();
    }
}
