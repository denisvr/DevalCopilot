using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Projects.ProjectVerificationInspection;

/// <summary>Records the text and the bound parameters of every statement a context executes, so a test proves the reads are SELECTs, never
/// select a column the contract forbids, and can replay a statement under EXPLAIN QUERY PLAN to prove it is indexed and bounded.</summary>
internal sealed class ProjectVerificationStatementLog : DbCommandInterceptor
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
