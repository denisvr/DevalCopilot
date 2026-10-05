using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>Owner-side writes and observation builders for the account-usage stop tests.</summary>
public static class AccountUsageStopTestSupport
{
    public static AccountUsageObservation Observation(
        DateTimeOffset at, int primary, int? secondary = null, string? bucket = "codex", bool providerReached = false,
        DateTimeOffset? primaryReset = null) =>
        AccountUsageObservation.Create(
            at,
            [new AccountUsageBucket(
                bucket,
                new AccountUsageWindow(primary, primaryReset),
                secondary is { } value ? new AccountUsageWindow(value, null) : null)],
            providerReached);

    /// <summary>The owner's set/clear through the real Domain method in its own context, as a separate request would.</summary>
    public static async Task SetStopAsync(SqliteDatabaseFixture fixture, Guid runId, int? percent)
    {
        await using var context = fixture.CreateContext();
        var run = await context.Runs.SingleAsync(candidate => candidate.Id == runId);
        run.SetCodexAccountUsageStopPercent(percent);
        context.Entry(run).Property(Run.CodexAccountUsageStopStorageProperty).IsModified = true;
        await context.SaveChangesAsync();
    }

    /// <summary>Overwrites the stored setting with a value of any SQLite storage class, the only way to persist what the Domain refuses.</summary>
    public static async Task SetStoredAsync(SqliteDatabaseFixture fixture, Guid runId, object? stored)
    {
        await using var context = fixture.CreateContext();
        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE runs SET CodexAccountUsageStopPercent = {stored} WHERE Id = {runId}");
    }

    public static async Task ChangeLaunchTargetAsync(SqliteDatabaseFixture fixture, string executablePath)
    {
        await using var context = fixture.CreateContext();
        await context.HostCapabilitySnapshots
            .Where(candidate => candidate.Capability == Capability.CodexCli)
            .ExecuteUpdateAsync(set => set.SetProperty(candidate => candidate.ResolvedExecutablePath, executablePath));
    }

    public static async Task<Attempt?> NewestAttemptAsync(SqliteDatabaseFixture fixture, Guid runId, AgentRole? role = null)
    {
        await using var context = fixture.CreateContext();
        var attempts = await context.Attempts.AsNoTracking()
            .Where(candidate => candidate.RunId == runId && candidate.Kind == AttemptKind.Agent)
            .ToListAsync();
        return attempts.Where(candidate => role is null || candidate.AgentRole == role).OrderByDescending(candidate => candidate.AttemptNumber).FirstOrDefault();
    }
}
