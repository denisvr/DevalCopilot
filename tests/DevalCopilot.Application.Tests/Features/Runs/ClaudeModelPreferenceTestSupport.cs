using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>Runs one caller-supplied action once, immediately before the first <c>SaveChangesAsync</c> or the first explicit
/// transaction of the context this interceptor is attached to, whichever comes first — after the handler has already read and guarded
/// its state, and before it holds the database write lock — to deterministically simulate a competing transaction committing in
/// exactly that window. A claim whose commit seam opens its own short transaction (ADR-0057) takes the write lock at that point, so a
/// competing connection can only commit before it; hooking the save instead would wait on the lock held by the handler itself.</summary>
public sealed class BeforeFirstSaveInterceptor(Func<Task> action) : SaveChangesInterceptor, IDbTransactionInterceptor
{
    private bool _ran;

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        await RunOnceAsync();
        return result;
    }

    public async ValueTask<InterceptionResult<System.Data.Common.DbTransaction>> TransactionStartingAsync(
        System.Data.Common.DbConnection connection,
        TransactionStartingEventData eventData,
        InterceptionResult<System.Data.Common.DbTransaction> result,
        CancellationToken cancellationToken = default)
    {
        await RunOnceAsync();
        return result;
    }

    private async Task RunOnceAsync()
    {
        if (!_ran)
        {
            _ran = true;
            await action();
        }
    }
}

/// <summary>Owner-side write of the run-scoped Claude model request through its own context,
/// as a separate request would perform it.</summary>
public static class ClaudeModelPreferenceTestSupport
{
    public static async Task SetPreferenceAsync(
        SqliteDatabaseFixture fixture, Guid runId, string? alias, string? effort = null)
    {
        await using var context = fixture.CreateContext();
        var run = await context.Runs.SingleAsync(candidate => candidate.Id == runId);
        run.SetRequestedClaudeModelRequest(alias, effort);
        await context.SaveChangesAsync();
    }

    public static async Task<string?> ReadAttemptModelAsync(SqliteDatabaseFixture fixture, Guid attemptId)
    {
        await using var context = fixture.CreateContext();
        return await context.Attempts.AsNoTracking()
            .Where(candidate => candidate.Id == attemptId)
            .Select(candidate => candidate.AgentRequestedModel)
            .SingleAsync();
    }

    public static async Task<string?> ReadAttemptEffortAsync(SqliteDatabaseFixture fixture, Guid attemptId)
    {
        await using var context = fixture.CreateContext();
        return await context.Attempts.AsNoTracking()
            .Where(candidate => candidate.Id == attemptId)
            .Select(candidate => candidate.AgentRequestedEffort)
            .SingleAsync();
    }
}
