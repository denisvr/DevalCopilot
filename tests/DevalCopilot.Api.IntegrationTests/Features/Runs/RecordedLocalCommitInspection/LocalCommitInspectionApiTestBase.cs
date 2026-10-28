using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Infrastructure.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.RecordedLocalCommitInspection;

/// <summary>One real host (hosted services removed by the shared factory) over a disposable migrated SQLite database whose Git adapter
/// ports are tripwired: the recorded read may reach neither. Helpers answer a read as its status, parsed body and raw text, and fingerprint
/// the stored operation rows.</summary>
public abstract class LocalCommitInspectionApiTestBase : IDisposable
{
    private readonly ApiWebApplicationFactory baseFactory = new();

    protected LocalCommitInspectionApiTestBase()
    {
        Host = baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ILocalCommitRepository>();
            services.AddSingleton<ILocalCommitRepository>(
                provider => Tripwire.Wrap<ILocalCommitRepository>(provider.GetRequiredService<LocalCommitGit>()));
            services.RemoveAll<ILocalCommitPreparer>();
            services.AddSingleton<ILocalCommitPreparer>(
                provider => Tripwire.Wrap<ILocalCommitPreparer>(provider.GetRequiredService<LocalCommitGit>()));
        }));
        Tripwire.Arm();
    }

    internal ExternalCallTripwire Tripwire { get; } = new();

    protected WebApplicationFactory<Program> Host { get; }

    public void Dispose()
    {
        Host.Dispose();
        baseFactory.Dispose();
        GC.SuppressFinalize(this);
    }

    protected static string Route(Guid runId, string query = "") => $"/api/runs/{runId}/local-commit-inspection{query}";

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

    protected Task<LocalCommitInspectionApiScene> SeedAsync(LocalCommitInspectionApiShape shape, string? reason = null) =>
        WithDbAsync(db => LocalCommitInspectionApiSeed.SeedAsync(db, shape, reason));

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

    /// <summary>The stored operation rows and the counts of every table a read could disturb, as one comparable value.</summary>
    protected Task<string> FingerprintAsync() => WithDbAsync(async db =>
    {
        var operations = await db.LocalCommitOperations.AsNoTracking().OrderBy(row => row.Id)
            .Select(row => new
            {
                row.Id,
                row.RunId,
                row.Status,
                row.CommitSha,
                row.CreatedAtUtc,
                row.ExecutionStartedAtUtc,
                row.CompletedAtUtc,
                row.OutcomeReasonCode,
            })
            .ToListAsync();
        return string.Join(";", operations.Select(row => string.Join("|", row.Id, row.RunId, row.Status, row.CommitSha, row.CreatedAtUtc,
            row.ExecutionStartedAtUtc, row.CompletedAtUtc, row.OutcomeReasonCode))) + "#" + await db.Projects.CountAsync() + "#" + await db.Runs.CountAsync() + "#"
            + await db.GitWorkspaces.CountAsync() + "#" + await db.GitCheckpoints.CountAsync() + "#" + await db.Events.CountAsync() + "#"
            + await db.Attempts.CountAsync() + "#" + await db.Artifacts.CountAsync();
    });
}
