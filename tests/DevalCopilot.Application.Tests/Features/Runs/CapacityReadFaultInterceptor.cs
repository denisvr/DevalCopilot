using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>Fails the global execution capacity policy's read inside a claim's write-locked transaction (ADR-0058): once a transaction has
/// started, the first read that classifies Agent contract-role pairs (only the capacity policy reads that column, so no earlier authority
/// or workspace-ownership read of the same transaction is ever touched) either cancels the caller's token or throws a database error. A
/// test therefore proves what a claim does when exactly that new final read fails, and <see cref="Fired"/> proves it was exercised.</summary>
public sealed class CapacityReadFaultInterceptor(FenceReadFault fault, CancellationTokenSource? source = null)
    : DbCommandInterceptor, IDbTransactionInterceptor
{
    private bool _armed;
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
        if (_armed && !_fired && command.CommandText.Contains("AgentResponseContract", StringComparison.Ordinal))
        {
            _fired = true;
            if (fault == FenceReadFault.Cancel)
            {
                source!.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }

            throw new SqliteException("Injected failure of the execution-capacity read.", 5);
        }

        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}
