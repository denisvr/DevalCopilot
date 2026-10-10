using System.Data.Common;
using DevalCopilot.Application.Features.Projects.Queries.GetProjectRunHistory;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Projects;

/// <summary>
/// ADR-0033 at the query boundary, over real file-backed SQLite: the structural bounds are refused rather than clamped, and the SQL the
/// handler issues is a fixed, read-only, indexed page (ordered descending, limited, with the cursor in the WHERE clause) that never
/// counts, offsets or loads the Run aggregate.
/// </summary>
public sealed class GetProjectRunHistoryQueryHandlerTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private sealed class RecordingInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Commands.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<object> ScalarExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
        {
            Commands.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            Commands.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    private async Task<Guid> SeedProjectAsync(int runs)
    {
        await using var dbContext = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "History", $@"C:\repos\{Guid.NewGuid():N}", DateTimeOffset.UtcNow);
        dbContext.Projects.Add(project);
        for (var index = 0; index < runs; index++)
        {
            var number = project.ReserveExecutionNumber();
            dbContext.Runs.Add(Run.RecordClassifiedIntent(
                Guid.NewGuid(), project.Id, number, RunExecutionMode.ManualAgent, $"Objective {number}", DateTimeOffset.UtcNow));
        }

        await dbContext.SaveChangesAsync();
        return project.Id;
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData(1, 1, true)]
    [InlineData(2147483647, 20, true)]
    [InlineData(5, 10, true)]
    [InlineData(0, null, false)]
    [InlineData(-1, null, false)]
    [InlineData(null, 0, false)]
    [InlineData(null, -1, false)]
    [InlineData(null, 21, false)]
    [InlineData(null, 2147483647, false)]
    [InlineData(3, 21, false)]
    public void The_structural_bounds_are_validated_and_never_clamped(int? before, int? limit, bool valid)
    {
        var result = new GetProjectRunHistoryQueryValidator().Validate(new GetProjectRunHistoryQuery(Guid.NewGuid(), before, limit));

        Assert.Equal(valid, result.IsValid);
    }

    [Fact]
    public async Task An_unknown_project_fails_with_the_stable_not_found_code_after_one_read()
    {
        var recorder = new RecordingInterceptor();
        await using var dbContext = _fixture.CreateContext(recorder);

        var result = await new GetProjectRunHistoryQueryHandler(dbContext).HandleAsync(
            new GetProjectRunHistoryQuery(Guid.NewGuid(), null, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("projects.not_found", result.Errors[0].Code);
        Assert.Single(recorder.Commands);
    }

    [Fact]
    public async Task A_page_is_three_indexed_selects_that_never_count_offset_write_or_load_the_run_aggregate()
    {
        var projectId = await SeedProjectAsync(25);
        var recorder = new RecordingInterceptor();
        await using var dbContext = _fixture.CreateContext(recorder);
        var handler = new GetProjectRunHistoryQueryHandler(dbContext);

        var first = await handler.HandleAsync(new GetProjectRunHistoryQuery(projectId, null, null), CancellationToken.None);
        var second = await handler.HandleAsync(new GetProjectRunHistoryQuery(projectId, first.Value.NextBeforeExecutionNumber, 7), CancellationToken.None);

        Assert.Equal(Enumerable.Range(16, 10).Reverse(), first.Value.Entries.Select(entry => entry.ExecutionNumber));
        Assert.Equal(16, first.Value.NextBeforeExecutionNumber);
        Assert.Equal(Enumerable.Range(9, 7).Reverse(), second.Value.Entries.Select(entry => entry.ExecutionNumber));
        Assert.True(second.Value.HasMore);
        Assert.Equal(9, second.Value.NextBeforeExecutionNumber);

        // Project existence, the runs page and the page's operations, twice; every one a SELECT.
        Assert.Equal(6, recorder.Commands.Count);
        Assert.All(recorder.Commands, command => Assert.StartsWith("SELECT", command.TrimStart(), StringComparison.OrdinalIgnoreCase));
        var pages = recorder.Commands.Where(command => command.Contains("FROM \"runs\"", StringComparison.Ordinal) && command.Contains("ORDER BY", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, pages.Count);
        Assert.All(pages, page =>
        {
            Assert.Contains("DESC", page, StringComparison.Ordinal);
            Assert.Contains("LIMIT", page, StringComparison.Ordinal);
            Assert.DoesNotContain("OFFSET", page, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("COUNT(", page, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("AccumulatedAutonomousSeconds", page, StringComparison.Ordinal);
            Assert.DoesNotContain("RequestedClaudeModel", page, StringComparison.Ordinal);
        });
        Assert.Contains("\"ExecutionNumber\" <", pages[1], StringComparison.Ordinal);
        Assert.DoesNotContain("\"ExecutionNumber\" <", pages[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_project_without_runs_reads_no_operations()
    {
        var projectId = await SeedProjectAsync(0);
        var recorder = new RecordingInterceptor();
        await using var dbContext = _fixture.CreateContext(recorder);

        var result = await new GetProjectRunHistoryQueryHandler(dbContext).HandleAsync(
            new GetProjectRunHistoryQuery(projectId, null, null), CancellationToken.None);

        Assert.Empty(result.Value.Entries);
        Assert.False(result.Value.HasMore);
        Assert.Null(result.Value.NextBeforeExecutionNumber);
        Assert.Equal(2, recorder.Commands.Count);
        Assert.DoesNotContain(recorder.Commands, command => command.Contains("local_commit_operations", StringComparison.Ordinal));
    }
}
