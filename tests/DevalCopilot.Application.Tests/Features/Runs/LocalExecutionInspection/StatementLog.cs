using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionInspection;

/// <summary>Records the text of every statement a context executes, so a test proves the reads are SELECTs and never select a column
/// the contract forbids.</summary>
internal sealed class StatementLog : DbCommandInterceptor
{
    public List<string> Statements { get; } = [];

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Statements.Add(command.CommandText);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Statements.Add(command.CommandText);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        Statements.Add(command.CommandText);
        return ValueTask.FromResult(result);
    }

    public void AssertReadOnly() =>
        Assert.All(Statements, statement => Assert.StartsWith("SELECT", statement.TrimStart(), StringComparison.Ordinal));

    public void AssertNeverSelects(params string[] columns)
    {
        foreach (var column in columns)
        {
            Assert.DoesNotContain(Statements, statement => statement.Contains(column, StringComparison.Ordinal));
        }
    }
}
