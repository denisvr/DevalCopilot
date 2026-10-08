using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DevalCopilot.Api.Features.Runs.GetLocalCommitStatus;
using DevalCopilot.Api.Features.Runs.RequestLocalCommit;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>Shared plumbing: one real Git scene, one SQLite file, and hosts over them (several over the same file model a restart).</summary>
public abstract partial class LocalCommitTestBase : IDisposable
{
    private readonly List<LocalCommitHost> _hosts = [];

    internal LocalCommitScene Scene { get; } = new();

    internal string DatabasePath { get; } = Path.Combine(Path.GetTempPath(), $"devalcopilot-local-commit-{Guid.NewGuid():N}.db");

    internal LocalCommitHost StartHost(
        bool runSupervisor,
        Func<ILocalCommitRepository, ILocalCommitRepository>? decorate = null,
        Func<ILocalCommitPreparer, ILocalCommitPreparer>? decoratePreparer = null,
        Action<IServiceCollection>? configure = null)
    {
        var host = new LocalCommitHost(Scene, DatabasePath, runSupervisor, decorate, decoratePreparer, configure);
        _hosts.Add(host);
        _ = host.Services;
        return host;
    }

    internal static async Task<HttpResponseMessage> PostAsync(
        LocalCommitHost host, Guid runId, LocalCommitLineageIds ids, Guid? operationId = null, string message = "Deliver the approved change",
        Guid? humanReviewId = null, Guid? reviewAttemptId = null, Guid? checkpointId = null)
    {
        using var client = LocalCommitLineage.AuthenticatedClient(host);
        return await client.PostAsJsonAsync(
            $"/api/runs/{runId}/local-commit",
            new RequestLocalCommitRequest
            {
                OperationId = operationId ?? Guid.NewGuid(),
                CheckpointId = checkpointId ?? ids.CheckpointId,
                CodeReviewAttemptId = reviewAttemptId ?? ids.ReviewAttemptId,
                HumanCheckpointReviewId = humanReviewId ?? ids.HumanReviewId,
                Message = message,
            });
    }

    internal static async Task<GetLocalCommitStatusResponse> StatusAsync(LocalCommitHost host, Guid runId)
    {
        using var client = LocalCommitLineage.AuthenticatedClient(host);
        var response = await client.GetAsync($"/api/runs/{runId}/local-commit");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<GetLocalCommitStatusResponse>())!;
    }

    internal static async Task<string> ErrorCodeAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(text);
        return document.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array
            ? errors[0].GetProperty("code").GetString()!
            : text;
    }

    internal static async Task<GetLocalCommitStatusResponse> WaitForAsync(
        LocalCommitHost host, Guid runId, Func<GetLocalCommitStatusResponse, bool> condition, int seconds = 30)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        GetLocalCommitStatusResponse status;
        do
        {
            status = await StatusAsync(host, runId);
            if (condition(status))
            {
                return status;
            }

            await Task.Delay(100);
        }
        while (DateTime.UtcNow < deadline);

        throw new Xunit.Sdk.XunitException($"Condition not reached; operation status {status.Operation?.Status ?? "none"}.");
    }

    internal static async Task<T> WithDbAsync<T>(LocalCommitHost host, Func<DevalCopilotDbContext, Task<T>> action)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>());
    }

    internal static Task<Run> RunAsync(LocalCommitHost host, Guid runId) =>
        WithDbAsync(host, db => db.Runs.AsNoTracking().SingleAsync(run => run.Id == runId));

    internal static Task<GitWorkspace> WorkspaceAsync(LocalCommitHost host, Guid workspaceId) =>
        WithDbAsync(host, db => db.GitWorkspaces.AsNoTracking().SingleAsync(workspace => workspace.Id == workspaceId));

    internal static Task<string[]> EventTypesAsync(LocalCommitHost host, Guid runId) =>
        WithDbAsync(host, async db => await db.Events.AsNoTracking().Where(runEvent => runEvent.RunId == runId)
            .OrderBy(runEvent => runEvent.Sequence).Select(runEvent => runEvent.EventType).ToArrayAsync());

    internal static Task<int> OperationCountAsync(LocalCommitHost host) =>
        WithDbAsync(host, db => db.LocalCommitOperations.AsNoTracking().CountAsync());

    /// <summary>A fresh context over the same database file, for reads that must not depend on any host being alive (for example
    /// right after a simulated host loss and before the next startup decides anything).</summary>
    internal DevalCopilotDbContext OpenDb() =>
        new(new DbContextOptionsBuilder<DevalCopilotDbContext>().UseSqlite($"Data Source={DatabasePath}").Options);

    internal async Task<LocalCommitOperation> OperationRowAsync(Guid runId)
    {
        await using var db = OpenDb();
        return await db.LocalCommitOperations.AsNoTracking().SingleAsync(operation => operation.RunId == runId);
    }

    internal async Task<Run> RunRowAsync(Guid runId)
    {
        await using var db = OpenDb();
        return await db.Runs.AsNoTracking().SingleAsync(run => run.Id == runId);
    }

    internal async Task<GitWorkspace> WorkspaceRowAsync(Guid workspaceId)
    {
        await using var db = OpenDb();
        return await db.GitWorkspaces.AsNoTracking().SingleAsync(workspace => workspace.Id == workspaceId);
    }

    internal async Task<string[]> EventTypeRowsAsync(Guid runId)
    {
        await using var db = OpenDb();
        return await db.Events.AsNoTracking().Where(runEvent => runEvent.RunId == runId)
            .OrderBy(runEvent => runEvent.Sequence).Select(runEvent => runEvent.EventType).ToArrayAsync();
    }

    internal async Task ExecuteSqlAsync(string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // Test-only SQL assembled from constants by the test itself.
        command.CommandText = sql;
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync();
    }

    public void Dispose()
    {
        foreach (var host in _hosts)
        {
            host.Dispose();
        }

        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={DatabasePath}"));
        if (File.Exists(DatabasePath))
        {
            File.Delete(DatabasePath);
        }

        Scene.Dispose();
    }
}
