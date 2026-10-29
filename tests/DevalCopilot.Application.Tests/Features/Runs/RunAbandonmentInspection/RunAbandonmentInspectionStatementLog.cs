using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.RunAbandonmentInspection;

/// <summary>Records the text and the bound parameters of every statement a context executes, so a test proves the read is one SELECT, never
/// selects a column the contract forbids, and can replay the statement under EXPLAIN QUERY PLAN.</summary>
internal sealed class RunAbandonmentInspectionStatementLog : DbCommandInterceptor
{
    public sealed record Recorded(string Text, IReadOnlyList<(string Name, object? Value)> Parameters);

    public List<Recorded> Commands { get; } = [];

    public IEnumerable<string> Statements => Commands.Select(command => command.Text);

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Record(command);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Record(command);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        Record(command);
        return ValueTask.FromResult(result);
    }

    public void AssertReadOnly() =>
        Assert.All(Statements, statement => Assert.StartsWith("SELECT", statement.TrimStart(), StringComparison.Ordinal));

    public void AssertNeverSelects(params string[] names)
    {
        foreach (var name in names)
        {
            Assert.DoesNotContain(Statements, statement => statement.Contains(name, StringComparison.Ordinal));
        }
    }

    private void Record(DbCommand command) => Commands.Add(new Recorded(
        command.CommandText,
        command.Parameters.Cast<DbParameter>().Select(parameter => (parameter.ParameterName, (object?)parameter.Value)).ToArray()));
}
