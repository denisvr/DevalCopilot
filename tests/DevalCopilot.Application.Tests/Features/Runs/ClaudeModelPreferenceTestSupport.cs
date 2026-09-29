using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>Runs one caller-supplied action immediately before the first <c>SaveChangesAsync</c>
/// of the context this interceptor is attached to — after the handler has already read and guarded
/// its state, and before its own transaction starts — to deterministically simulate a competing
/// transaction committing in exactly that window.</summary>
public sealed class BeforeFirstSaveInterceptor(Func<Task> action) : SaveChangesInterceptor
{
    private bool _ran;

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (!_ran)
        {
            _ran = true;
            await action();
        }

        return result;
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
