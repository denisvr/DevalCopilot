using System.Data.Common;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// Owner-side writes and Domain-built persisted history for the token-activity stop tests. History
/// attempts use the real Domain factories, are dispatched and concluded with a neutral failed
/// invocation outcome (so no collaboration message or eligibility side effect exists), and occupy
/// consecutive attempt numbers and budget slots starting at the number passed in.
/// </summary>
public static class TokenStopTestSupport
{
    public const string Fingerprint = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    public static AgentTokenUsageEvidence CodexUsage(int input, int output) =>
        AgentTokenUsageEvidence.Create(input, output, null, null, AgentTokenUsageEvidencePolicy.CodexCliSchemaVersion);

    public static AgentTokenUsageEvidence ClaudeUsage(int input, int output, int? creation, int? read) =>
        AgentTokenUsageEvidence.Create(input, output, creation, read, AgentTokenUsageEvidencePolicy.ClaudeCliSchemaVersion);

    /// <summary>The owner's set/clear, performed through the real Domain method in its own context
    /// exactly as a separate request would.</summary>
    public static async Task SetStopAsync(SqliteDatabaseFixture fixture, Guid runId, AgentProvider provider, long? threshold)
    {
        await using var context = fixture.CreateContext();
        var run = await context.Runs.SingleAsync(candidate => candidate.Id == runId);
        run.SetTokenStopThreshold(provider, threshold);
        await context.SaveChangesAsync();
    }

    /// <summary>Builds one dispatched, concluded Agent attempt for <paramref name="provider"/>.</summary>
    public static Attempt ConcludedHistory(
        Guid runId, Guid workspaceId, Guid checkpointId, int number, AgentProvider provider, AgentTokenUsageEvidence? usage)
    {
        var attempt = Claim(runId, workspaceId, checkpointId, number, provider);
        attempt.MarkAgentDispatched(Now);
        attempt.CompleteAgent(AgentOutcome.ProviderInvocationFailed, Fingerprint, Now, tokenUsage: usage);
        return attempt;
    }

    /// <summary>Builds one dispatched Agent attempt that is still Running (its usage is pending).</summary>
    public static Attempt RunningHistory(Guid runId, Guid workspaceId, Guid checkpointId, int number, AgentProvider provider)
    {
        var attempt = Claim(runId, workspaceId, checkpointId, number, provider);
        attempt.MarkAgentDispatched(Now);
        return attempt;
    }

    /// <summary>Builds one claimed Agent attempt that never reached dispatch and failed, so it
    /// consumes a budget slot but never invoked a provider.</summary>
    public static Attempt UndispatchedHistory(
        Guid runId, Guid workspaceId, Guid checkpointId, int number, AgentProvider provider)
    {
        var attempt = Claim(runId, workspaceId, checkpointId, number, provider);
        attempt.Fail(Now);
        return attempt;
    }

    public static async Task AddAsync(SqliteDatabaseFixture fixture, params Attempt[] attempts)
    {
        await using var context = fixture.CreateContext();
        context.Attempts.AddRange(attempts);
        await context.SaveChangesAsync();
    }

    /// <summary>Overwrites one persisted attempt's provider or usage columns directly, the only way
    /// to persist the malformed, unsupported, or unattributed rows the Domain itself refuses to
    /// record.</summary>
    public static async Task CorruptAsync(SqliteDatabaseFixture fixture, Guid attemptId, string assignments)
    {
        await using var context = fixture.CreateContext();
#pragma warning disable EF1002 // Fixed, test-owned assignment text; no external input.
        await context.Database.ExecuteSqlRawAsync($"UPDATE attempts SET {assignments} WHERE Id = {{0}}", attemptId);
#pragma warning restore EF1002
    }

    /// <summary>Adds one dispatched, concluded history attempt for <paramref name="provider"/> at the
    /// run's next attempt number and budget slot, so it never collides with a scenario's own
    /// attempts or with the claim under test.</summary>
    public static async Task AddHistoryAsync(
        SqliteDatabaseFixture fixture, Guid runId, Guid workspaceId, Guid checkpointId, AgentProvider provider,
        AgentTokenUsageEvidence? usage)
    {
        await using var context = fixture.CreateContext();
        var next = await context.Attempts.CountAsync(candidate => candidate.RunId == runId) + 1;
        context.Attempts.Add(ConcludedHistory(runId, workspaceId, checkpointId, next, provider, usage));
        await context.SaveChangesAsync();
    }

    /// <summary>Counts evidence captures, so a test can prove a refused claim did no Git work.</summary>
    public sealed class CountingEvidenceReader(GitWorkspaceEvidenceResult result) : IGitWorkspaceEvidenceReader
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(result);
        }
    }

    /// <summary>Counts every reader command EF Core sends, so a test can prove an unconfigured stop
    /// issues no query at all.</summary>
    public sealed class ReaderCountingInterceptor : DbCommandInterceptor
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private static Attempt Claim(Guid runId, Guid workspaceId, Guid checkpointId, int number, AgentProvider provider) =>
        provider == AgentProvider.Codex
            ? Attempt.ClaimAgent(
                Guid.NewGuid(), runId, number, workspaceId, checkpointId, Fingerprint, Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 262144, 524288, Now, number)
            : Attempt.ClaimAgentCriticalReview(
                Guid.NewGuid(), runId, number, workspaceId, checkpointId, Fingerprint, Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 262144, 524288, Now, number);
}
