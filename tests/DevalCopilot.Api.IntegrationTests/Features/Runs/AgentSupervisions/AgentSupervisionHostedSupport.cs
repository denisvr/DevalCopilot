using DevalCopilot.Api.HostedServices;
using DevalCopilot.Application.Features.Runs.Policies.AgentSupervisions;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.AgentSupervisions;

internal static class AgentSupervisionHostedSupport
{
    public static void Register(ServiceCollection services, TimeProvider? clock, AgentSupervisionHostInstance host)
    {
        if (clock is not null)
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton(clock);
        }

        services.AddSingleton(host);
        services.AddSingleton<AgentSupervisionObserver>();
    }

    public static AgentSupervisionSweep CreateSweep(ServiceProvider provider) => new(
        provider.GetRequiredService<IServiceScopeFactory>(),
        provider.GetRequiredService<TimeProvider>(),
        NullLogger<AgentSupervisionSweep>.Instance);

    public static async Task<AgentSupervision?> ReceiptAsync(ServiceProvider provider, Guid attemptId)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        return await db.AgentSupervisions.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.AttemptId == attemptId);
    }

    public static async Task<Attempt> AttemptAsync(ServiceProvider provider, Guid attemptId)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        return await db.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attemptId);
    }

    public static async Task<List<RunEvent>> SupervisionEventsAsync(ServiceProvider provider, Guid runId)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        return (await db.Events.AsNoTracking().Where(candidate => candidate.RunId == runId).ToListAsync())
            .Where(candidate => candidate.EventType.StartsWith("agent_supervision.", StringComparison.Ordinal))
            .OrderBy(candidate => candidate.Sequence)
            .ToList();
    }

    public static async Task<T> WaitAsync<T>(Func<Task<T?>> read, TimeSpan? timeout = null) where T : class
    {
        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(20));
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await read() is { } value)
            {
                return value;
            }

            await Task.Delay(50);
        }

        throw new Xunit.Sdk.XunitException("The expected observation fact did not appear in time.");
    }

    public static async Task<Attempt> WaitForTerminalAsync(ServiceProvider provider, Guid attemptId, TimeSpan? timeout = null) =>
        await WaitAsync(
            async () =>
            {
                var attempt = await AttemptAsync(provider, attemptId);
                return attempt.Status == AttemptStatus.Running ? null : attempt;
            },
            timeout);

    public static int InvocationCount(object adapter)
    {
        var property = adapter.GetType().GetProperty("InvocationCount")
            ?? throw new InvalidOperationException("The adapter double exposes no InvocationCount.");
        return (int)property.GetValue(adapter)!;
    }

    public static void ResetDatabase(string databasePath)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearPool(new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={databasePath}"));
        foreach (var path in new[] { databasePath, databasePath + "-wal", databasePath + "-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    public static async Task ExecuteRawAsync(string databasePath, string sql)
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // Test-only SQL assembled from constants this test generated.
        command.CommandText = sql;
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync();
    }

    public static async Task StopAsync(IHostedService service) =>
        await service.StopAsync(new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token);

    internal static void AssertCleanTermination(AgentSupervision receipt, AgentSupervisionHostInstance host)
    {
        Assert.Equal(host.Id, receipt.HostInstanceId);
        Assert.NotNull(receipt.ClosedAtUtc);
        Assert.Equal(AgentSupervisionClosureReason.AttemptTerminal, receipt.ClosureReason);
    }
}
