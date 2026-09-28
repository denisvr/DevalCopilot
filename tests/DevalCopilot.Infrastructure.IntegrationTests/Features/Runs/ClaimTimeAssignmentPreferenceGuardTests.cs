using System.Data.Common;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Runs;

/// <summary>
/// Proves the two primitives every <c>Create*Attempt</c> claim handler shares
/// (<c>IDevalCopilotDbContext.BeginTransactionAsync</c> and
/// <see cref="CurrentCodexAssignmentPreference.ConfirmUnchangedAsync"/>) make the guard check and
/// the claimed Attempt's own durable commit one genuinely atomic database operation — not merely
/// two independently-atomic statements with an unguarded gap between them, which an earlier
/// correction round left open (a conditional <c>ExecuteUpdateAsync</c> alone is atomic only for
/// its own statement; nothing prevented a preference-only change from landing between it and the
/// following <c>SaveChangesAsync</c>).
///
/// <para>
/// The competing write below is started on a background <see cref="Task"/> — never synchronously
/// awaited while the claim-side transaction deliberately still holds its own write lock, which
/// would self-deadlock this single test method. A <see cref="WriteAttemptSignalInterceptor"/>
/// attached only to that competing connection signals a <see cref="TaskCompletionSource"/> from
/// inside EF Core's own command-interception callback, filtered to the exact moment its own
/// `UPDATE` command — never its preceding `SELECT` — is about to execute against the database:
/// never merely inferred from "the background Task was started" or from a fixed delay guessed to
/// be "probably enough." Because that callback fires immediately before the command runs, checking
/// completion right after the signal alone would read "not completed" whether or not anything is
/// actually blocking it; a short, generously-margined confirmation window after the signal (200 ms,
/// versus a normal unblocked write's low single-digit milliseconds) is what turns "reached the
/// write" into a genuine "still blocked" assertion. The competing connection also carries a short,
/// explicit <c>Default Timeout</c> so its own lock-wait is itself bounded and the test completes
/// quickly. A second, no-lock test below is this methodology's own negative control: the exact same
/// interceptor, signal, and confirmation window, but with no competing claim transaction ever
/// opened, proving the mechanism can also report "not blocked" rather than trivially always
/// reporting "still blocked" regardless of real contention.
/// </para>
/// </summary>
public sealed class ClaimTimeAssignmentPreferenceGuardTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 18, 0, 0, TimeSpan.Zero);
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-claim-guard-{Guid.NewGuid():N}.db");

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        // Two distinct connection strings were used against this same file (the plain one, and
        // the racing write's own with an appended Default Timeout) — each is its own pool bucket,
        // and both must be cleared before the file can be deleted.
        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_databasePath}"));
        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_databasePath};Default Timeout=5"));
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_preference_only_write_started_after_the_guard_but_before_the_attempt_commit_never_produces_a_stale_attempt()
    {
        await using (var migrate = CreateContext())
        {
            await migrate.Database.MigrateAsync();
        }

        var runId = await SeedRunAsync("gpt-5-legacy", "low");

        await using var claimSideContext = CreateContext();
        await using var claimTransaction = await claimSideContext.BeginTransactionAsync(CancellationToken.None);

        var (requestedModel, requestedEffort) = await CurrentCodexAssignmentPreference.ReadAsync(claimSideContext, runId, CancellationToken.None);
        Assert.Equal("gpt-5-legacy", requestedModel);
        Assert.Equal("low", requestedEffort);

        var confirmed = await CurrentCodexAssignmentPreference.ConfirmUnchangedAsync(
            claimSideContext, runId, requestedModel, requestedEffort, CancellationToken.None);
        Assert.True(confirmed);

        // Started on a background Task — never synchronously awaited here — exactly like the
        // production handlers, so this test never awaits a competing writer while the claim's own
        // transaction still holds its write lock. Its own short Default Timeout (5s) bounds how
        // long it will keep retrying before giving up.
        var reachedSelect = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reachedWriteAttempt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAfterSelect = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var racingWriteTask = Task.Run(async () =>
        {
            await using var racingContext = CreateContext(defaultTimeoutSeconds: 5, new WriteAttemptSignalInterceptor(reachedSelect, reachedWriteAttempt));
            var racingRun = await racingContext.Runs.SingleAsync(candidate => candidate.Id == runId);

            // Held open only long enough for the assertion below to observe that the SELECT above
            // did not itself trigger reachedWriteAttempt — never awaited by the claim side, so this
            // gate cannot itself introduce a deadlock with the claim transaction's own lock.
            await releaseAfterSelect.Task;

            racingRun.SetRequestedCodexAssignment("gpt-6-sol", "high");
            await racingContext.SaveChangesAsync(CancellationToken.None);
        });

        // Bounded synchronization, step one: wait for proof the competing connection's own initial
        // read (`SingleAsync`) has executed — signaled from inside EF Core's own command-
        // interception callback, filtered to a `SELECT` command specifically.
        await reachedSelect.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // The interceptor's own filtering is asserted directly, not merely inferred from source
        // review: the SELECT already ran (the wait above just proved it), and it must not have
        // triggered the write-attempt signal — only the later UPDATE the interceptor specifically
        // watches for may do that.
        Assert.False(reachedWriteAttempt.Task.IsCompleted);

        // Only now is the racing task allowed to proceed to its own UPDATE.
        releaseAfterSelect.TrySetResult();

        // Bounded synchronization, step two: wait for proof the competing write's own UPDATE
        // command is genuinely about to execute against the database — signaled from inside EF
        // Core's own command-interception callback, not inferred from "the background Task was
        // started" or from a fixed delay guessed to be "probably enough." The wait itself is
        // bounded (5s) so a failure to ever reach that point fails this test promptly rather than
        // hanging.
        await reachedWriteAttempt.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Bounded synchronization, step three: the interception callback fires immediately before
        // the command executes, so checking completion right after the signal alone would trivially
        // read "not completed" every time, blocked or not — that is not yet a discriminating
        // assertion. A short, generously-margined confirmation window (200 ms — an unblocked local
        // SQLite write of this size normally completes in low single-digit milliseconds) gives the
        // competing write a fair chance to finish if nothing is actually holding it up.
        await Task.Delay(TimeSpan.FromMilliseconds(200));

        // The discriminating assertion: after that fair chance to finish, the competing write must
        // still be blocked by this still-open claim transaction's own lock, never having silently
        // landed already. Without the fix (the guard and the Attempt commit as two independently-
        // atomic operations rather than one), nothing would hold a lock here and the race would
        // already have completed within the confirmation window above.
        Assert.False(racingWriteTask.IsCompleted);

        var attemptId = Guid.NewGuid();
        var claimedAttempt = Attempt.ClaimAgentWithAssignment(
            attemptId, runId, 1, Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now, requestedModel, requestedEffort, agentBudgetSlot: 1);
        claimSideContext.Attempts.Add(claimedAttempt);
        await claimSideContext.SaveChangesAsync(CancellationToken.None);
        await claimTransaction.CommitAsync(CancellationToken.None);

        // Only now, after the claim's own commit, is the racing write awaited — proving it, not
        // this test, decides when the competing write is allowed to actually land.
        await racingWriteTask;

        await using var verify = CreateContext();
        var persistedAttempt = await verify.Attempts.SingleAsync(candidate => candidate.Id == attemptId);
        Assert.Equal("gpt-5-legacy", persistedAttempt.AgentRequestedModel);
        Assert.Equal("low", persistedAttempt.AgentRequestedEffort);

        // The race was blocked for the claim's entire hold and applied only afterward — a
        // temporally correct ordering (strictly after the whole claim transaction), never a value
        // torn into the middle of it. The already-committed Attempt above still carries the pair
        // that was actually current at its own claim boundary, regardless of what the Run's
        // preference has since become.
        var runAfter = await verify.Runs.SingleAsync(candidate => candidate.Id == runId);
        Assert.Equal("gpt-6-sol", runAfter.RequestedCodexModel);
        Assert.Equal("high", runAfter.RequestedCodexEffort);
    }

    /// <summary>
    /// The positive test above's own negative control: the identical interceptor, signal, and
    /// 200 ms confirmation window, but with no competing claim transaction ever opened. Proves the
    /// methodology can report "not blocked" too — that <c>Assert.False(racingWriteTask.IsCompleted)</c>
    /// above is a genuine, two-sided discriminator, not a check that would trivially pass regardless
    /// of whether anything actually held a lock.
    /// </summary>
    [Fact]
    public async Task A_preference_only_write_with_no_competing_claim_transaction_completes_promptly()
    {
        await using (var migrate = CreateContext())
        {
            await migrate.Database.MigrateAsync();
        }

        var runId = await SeedRunAsync("gpt-5-legacy", "low");

        var selectSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var updateSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var racingContext = CreateContext(defaultTimeoutSeconds: 5, new WriteAttemptSignalInterceptor(selectSignal, updateSignal));
        var racingRun = await racingContext.Runs.SingleAsync(candidate => candidate.Id == runId);
        racingRun.SetRequestedCodexAssignment("gpt-6-sol", "high");

        // No claim-side transaction is opened anywhere in this test — nothing here holds any lock
        // this write could contend with.
        var writeTask = racingContext.SaveChangesAsync(CancellationToken.None);

        await updateSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // The identical confirmation window the positive test uses — here, with nothing blocking
        // the write, it must be enough time for the write to actually finish.
        await Task.Delay(TimeSpan.FromMilliseconds(200));

        Assert.True(writeTask.IsCompleted);
        await writeTask;

        await using var verify = CreateContext();
        var runAfter = await verify.Runs.SingleAsync(candidate => candidate.Id == runId);
        Assert.Equal("gpt-6-sol", runAfter.RequestedCodexModel);
        Assert.Equal("high", runAfter.RequestedCodexEffort);
    }

    private DevalCopilotDbContext CreateContext(int? defaultTimeoutSeconds = null, IInterceptor? interceptor = null)
    {
        var connectionString = defaultTimeoutSeconds is { } seconds
            ? $"Data Source={_databasePath};Default Timeout={seconds}"
            : $"Data Source={_databasePath}";
        var optionsBuilder = new DbContextOptionsBuilder<DevalCopilotDbContext>().UseSqlite(connectionString);
        if (interceptor is not null)
        {
            optionsBuilder.AddInterceptors(interceptor);
        }

        return new DevalCopilotDbContext(optionsBuilder.Options);
    }

    private async Task<Guid> SeedRunAsync(string requestedModel, string requestedEffort)
    {
        await using var context = CreateContext();
        var project = Project.Register(Guid.NewGuid(), "Claim-time guard", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Prove the claim-time guard", Now);
        run.Claim(Now);
        run.SetRequestedCodexAssignment(requestedModel, requestedEffort);

        context.Projects.Add(project);
        context.Runs.Add(run);
        await context.SaveChangesAsync(CancellationToken.None);

        return run.Id;
    }

    /// <summary>
    /// Signals the moment the competing connection's own <c>SELECT</c> or <c>UPDATE</c> statement is
    /// genuinely about to execute against the database — from inside EF Core's own command-
    /// interception callback, immediately before the underlying ADO.NET command runs — rather than
    /// merely "the background Task was scheduled" or "we are about to call SaveChangesAsync". Each
    /// signal is filtered by the command's own text, never fired unconditionally on every command:
    /// <paramref name="updateSignal"/> must fire only for the preference <c>UPDATE</c>
    /// <c>SaveChangesAsync</c> issues, never for the preceding <c>SELECT</c> the initial
    /// <c>SingleAsync</c> read issues, and <paramref name="selectSignal"/> is the converse — proving
    /// that distinction rather than assuming it.
    /// </summary>
    private sealed class WriteAttemptSignalInterceptor(TaskCompletionSource selectSignal, TaskCompletionSource updateSignal) : DbCommandInterceptor
    {
        // The Sqlite provider executes an UPDATE issued by SaveChangesAsync through
        // ExecuteReaderAsync (to retrieve the affected row count), not ExecuteNonQueryAsync —
        // every execution path is therefore intercepted here, so whichever path either statement
        // actually takes is covered.
        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            SignalForCommandText(command.CommandText);
            return base.NonQueryExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            SignalForCommandText(command.CommandText);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            SignalForCommandText(command.CommandText);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            SignalForCommandText(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
        {
            SignalForCommandText(command.CommandText);
            return base.ScalarExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
        {
            SignalForCommandText(command.CommandText);
            return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void SignalForCommandText(string commandText)
        {
            var trimmed = commandText.TrimStart();
            if (trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                selectSignal.TrySetResult();
            }
            else if (trimmed.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase))
            {
                updateSignal.TrySetResult();
            }
        }
    }
}
