using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>Fails the workspace-execution policy's final read inside a claim's write-locked transaction (ADR-0057): once a transaction
/// has started, the policy's last read (the active-lease table, reached only after the policy counted verification executions inside that
/// transaction) either cancels the caller's token or throws a database error. Nothing else is touched, so a test proves what a
/// claim does when exactly that new read fails.</summary>
public sealed class FenceReadFaultInterceptor(FenceReadFault fault, CancellationTokenSource? source = null) : DbCommandInterceptor, IDbTransactionInterceptor
{
    private bool _armed;
    private bool _policyReached;
    private bool _fired;

    /// <summary>Whether the fault was actually injected, so a test cannot pass without ever exercising the read.</summary>
    public bool Fired => _fired;

    public ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection, TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
    {
        _armed = true;
        return ValueTask.FromResult(result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (_armed && command.CommandText.Contains("verification_executions", StringComparison.Ordinal))
        {
            // Only the policy counts verification executions, so the lease read that follows is the policy's own, never an earlier
            // authority read of the same transaction.
            _policyReached = true;
        }

        if (_armed && _policyReached && !_fired && command.CommandText.Contains("repository_mutation_leases", StringComparison.Ordinal))
        {
            _fired = true;
            if (fault == FenceReadFault.Cancel)
            {
                source!.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }

            throw new SqliteException("Injected failure of the workspace-execution read.", 5);
        }

        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}
