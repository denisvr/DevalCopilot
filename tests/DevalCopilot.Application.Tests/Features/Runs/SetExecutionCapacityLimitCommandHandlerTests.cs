using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs.Commands.SetExecutionCapacityLimit;
using DevalCopilot.Application.Features.Runs.Policies.ExecutionCapacity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// ADR-0059's one explicit operator command against real file-backed SQLite through independent connections: a change appends exactly one
/// next immutable revision, the same value is an idempotent no-op that repairs nothing, a limit below the coherent recorded owners is
/// refused without being saved as a pending request, unreadable configuration or ownership and revision overflow refuse with nothing
/// written, and no owner row is ever changed by a setting.
/// </summary>
public sealed class SetExecutionCapacityLimitCommandHandlerTests : IAsyncLifetime
{
    private const string BelowOwners = "execution_capacity.limit_below_recorded_owners";
    private const string Unavailable = "execution_capacity.unavailable";

    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    private async Task<Result<SetExecutionCapacityLimitCommandResult>> SetAsync(int limit)
    {
        await using var db = fixture.CreateContext();
        return await new SetExecutionCapacityLimitCommandHandler(db)
            .HandleAsync(new SetExecutionCapacityLimitCommand(limit), CancellationToken.None);
    }

    private async Task<string> HistoryAsync()
    {
        await using var db = fixture.CreateContext();
        return string.Join(
            ',',
            await db.ExecutionCapacityConfigurations.AsNoTracking().OrderBy(row => row.Revision)
                .Select(row => row.Revision + ":" + row.Limit).ToListAsync());
    }

    private async Task<string> OwnerFactsAsync()
    {
        await using var db = fixture.CreateContext();
        var attempts = await db.Attempts.AsNoTracking().OrderBy(a => a.Id).Select(a => a.Id + ":" + a.Status).ToListAsync();
        var executions = await db.VerificationExecutions.AsNoTracking().OrderBy(e => e.Id).Select(e => e.Id + ":" + e.Status).ToListAsync();
        var operations = await db.LocalCommitOperations.AsNoTracking().OrderBy(o => o.Id).Select(o => o.Id + ":" + o.Status).ToListAsync();
        var workspaces = await db.GitWorkspaces.AsNoTracking().OrderBy(w => w.Id).Select(w => w.Id + ":" + w.Status).ToListAsync();
        return string.Join(';', attempts.Concat(executions).Concat(operations).Concat(workspaces));
    }

    private static void AssertReading(
        Result<SetExecutionCapacityLimitCommandResult> result, ExecutionCapacityStatus status, int limit, int owners, string? reason)
    {
        Assert.True(result.IsSuccess, string.Join(',', result.Errors.Select(error => error.Code)));
        Assert.Equal(new SetExecutionCapacityLimitCommandResult(status, limit, owners, reason), result.Value);
    }

    [Fact]
    public async Task Lowering_with_no_owner_appends_exactly_one_next_revision_and_reports_the_new_limit()
    {
        var result = await SetAsync(1);

        AssertReading(result, ExecutionCapacityStatus.Available, 1, 0, null);
        Assert.Equal("1:2,2:1", await HistoryAsync());
    }

    [Fact]
    public async Task Lowering_to_one_beside_exactly_one_owner_is_allowed_and_reports_capacity_reached()
    {
        await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Verification);
        var before = await OwnerFactsAsync();

        var result = await SetAsync(1);

        AssertReading(result, ExecutionCapacityStatus.AtCapacity, 1, 1, "execution_capacity_limit_reached");
        Assert.Equal("1:2,2:1", await HistoryAsync());
        Assert.Equal(before, await OwnerFactsAsync());
    }

    [Fact]
    public async Task Raising_back_to_two_appends_a_further_revision_and_opens_the_second_place()
    {
        await SetAsync(1);
        await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Agent);

        var result = await SetAsync(2);

        AssertReading(result, ExecutionCapacityStatus.Available, 2, 1, null);
        Assert.Equal("1:2,2:1,3:2", await HistoryAsync());
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(2, 2)]
    [InlineData(1, 1)]
    public async Task The_same_value_is_a_no_op_that_appends_nothing_repairs_nothing_and_releases_nothing(int limit, int owners)
    {
        if (limit == 1)
        {
            await SetAsync(1);
        }

        for (var index = 0; index < owners; index++)
        {
            await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Agent);
        }

        var historyBefore = await HistoryAsync();
        var factsBefore = await OwnerFactsAsync();

        var result = await SetAsync(limit);

        Assert.True(result.IsSuccess);
        Assert.Equal(limit, result.Value.Limit);
        Assert.Equal(owners, result.Value.RecordedOwners);
        Assert.Equal(historyBefore, await HistoryAsync());
        Assert.Equal(factsBefore, await OwnerFactsAsync());
    }

    [Theory]
    [InlineData(OwnerFamily.Agent, OwnerFamily.Verification)]
    [InlineData(OwnerFamily.Verification, OwnerFamily.LocalCommit)]
    [InlineData(OwnerFamily.LocalCommit, OwnerFamily.Agent)]
    public async Task Lowering_two_to_one_while_two_owners_exist_is_refused_with_nothing_written_or_saved_as_pending(
        OwnerFamily first, OwnerFamily second)
    {
        await ExecutionCapacitySeed.AddAsync(fixture, first, second);
        var factsBefore = await OwnerFactsAsync();

        var result = await SetAsync(1);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal(BelowOwners, error.Code);
        Assert.Equal(ErrorType.Conflict, error.Type);
        Assert.Equal("1:2", await HistoryAsync());
        Assert.Equal(factsBefore, await OwnerFactsAsync());

        // Nothing was saved as a pending reduction: once recorded ownership changes the operator may try again explicitly.
        Assert.Equal(2, (await SetAsync(2)).Value.Limit);
        Assert.Equal("1:2", await HistoryAsync());
    }

    [Fact]
    public async Task A_freed_owner_lets_the_operator_try_the_lowering_again()
    {
        var owners = await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Agent, OwnerFamily.Verification);
        Assert.Equal(BelowOwners, Assert.Single((await SetAsync(1)).Errors).Code);

        await ExecutionCapacitySeed.FinishAsync(fixture, owners[1]);

        AssertReading(await SetAsync(1), ExecutionCapacityStatus.AtCapacity, 1, 1, "execution_capacity_limit_reached");
        Assert.Equal("1:2,2:1", await HistoryAsync());
    }

    [Fact]
    public async Task Untrustworthy_recorded_ownership_refuses_every_change_as_unavailable_and_is_not_repaired()
    {
        var other = await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Agent);
        await using (var db = fixture.CreateContext())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE attempts SET AgentRole = {"CodeReviewer"} WHERE Id = {other.Id}");
        }

        var factsBefore = await OwnerFactsAsync();
        var result = await SetAsync(1);

        Assert.Equal(Unavailable, Assert.Single(result.Errors).Code);
        Assert.Equal(ErrorType.Conflict, Assert.Single(result.Errors).Type);
        Assert.Equal("1:2", await HistoryAsync());
        Assert.Equal(factsBefore, await OwnerFactsAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_or_malformed_configuration_refuses_every_change_as_unavailable_and_is_never_reseeded(bool malformed)
    {
        if (malformed)
        {
            await RawConfigurationRows.InsertAsync(fixture.ConnectionString, 2L, 7L, true);
        }
        else
        {
            await RawConfigurationRows.DeleteAllAsync(fixture.ConnectionString);
        }

        var before = await RawRowCountAsync();
        var result = await SetAsync(1);

        Assert.Equal(Unavailable, Assert.Single(result.Errors).Code);
        Assert.Equal(before, await RawRowCountAsync());
    }

    [Fact]
    public async Task A_revision_that_would_overflow_refuses_the_change_without_writing()
    {
        await RawConfigurationRows.InsertAsync(fixture.ConnectionString, long.MaxValue, 2L, false);

        var result = await SetAsync(1);

        Assert.Equal(Unavailable, Assert.Single(result.Errors).Code);
        Assert.Equal("1:2,9223372036854775807:2", await HistoryAsync());
    }

    [Fact]
    public async Task Concurrent_explicit_changes_are_serialized_and_the_last_committed_change_is_current()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(index => Task.Run(() => SetAsync(index % 2 == 0 ? 1 : 2))));

        Assert.All(results, result => Assert.True(result.IsSuccess, string.Join(',', result.Errors.Select(error => error.Code))));
        var history = (await HistoryAsync()).Split(',');
        var revisions = history.Select(row => long.Parse(row.Split(':')[0], System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        Assert.Equal(Enumerable.Range(1, revisions.Length).Select(value => (long)value).ToArray(), revisions);
        for (var index = 1; index < history.Length; index++)
        {
            Assert.NotEqual(history[index - 1].Split(':')[1], history[index].Split(':')[1]);
        }

        await using var db = fixture.CreateContext();
        var reading = await ExecutionCapacityPolicy.ReadAsync(db, null, CancellationToken.None);
        Assert.Equal(int.Parse(history[^1].Split(':')[1], System.Globalization.CultureInfo.InvariantCulture), reading.Limit);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void The_validator_refuses_every_limit_other_than_one_or_two(int limit)
    {
        var validation = new SetExecutionCapacityLimitCommandValidator().Validate(new SetExecutionCapacityLimitCommand(limit));

        Assert.False(validation.IsValid);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void The_validator_accepts_one_and_two(int limit) =>
        Assert.True(new SetExecutionCapacityLimitCommandValidator().Validate(new SetExecutionCapacityLimitCommand(limit)).IsValid);

    private async Task<long> RawRowCountAsync()
    {
        await using var connection = new SqliteConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM execution_capacity_configurations;";
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
