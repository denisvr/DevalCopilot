using DevalCopilot.Application.Features.Runs.Policies.ExecutionCapacity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// ADR-0059's durable configuration read against real file-backed SQLite through independent connections: the migration's seeded revision
/// one with limit two is the default, the latest revision is the current setting and is read fresh despite tracked state, a missing or
/// malformed latest revision is Unavailable without a limit and never falls back to older history, and an ownership reading that cannot be
/// trusted keeps the validated limit. The status of a reading follows the persisted limit.
/// </summary>
public sealed class ExecutionCapacityConfigurationTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    private async Task<ExecutionCapacityReading> ReadAsync()
    {
        await using var db = fixture.CreateContext();
        return await ExecutionCapacityPolicy.ReadAsync(db, null, CancellationToken.None);
    }


    [Fact]
    public async Task A_migrated_database_defaults_to_revision_one_with_limit_two()
    {
        var reading = await ReadAsync();

        Assert.Equal(new ExecutionCapacityReading(ExecutionCapacityStatus.Available, 0, 2, 1), reading);
    }

    [Fact]
    public async Task The_latest_revision_is_the_current_setting_and_older_history_is_retained()
    {
        await ExecutionCapacitySeed.SetLimitAsync(fixture, 1);
        await ExecutionCapacitySeed.SetLimitAsync(fixture, 2);
        await ExecutionCapacitySeed.SetLimitAsync(fixture, 1);

        var reading = await ReadAsync();

        Assert.Equal(1, reading.Limit);
        Assert.Equal(4, reading.Revision);
        await using var db = fixture.CreateContext();
        var history = await db.ExecutionCapacityConfigurations.AsNoTracking().OrderBy(row => row.Revision)
            .Select(row => row.Revision + ":" + row.Limit).ToListAsync();
        Assert.Equal(["1:2", "2:1", "3:2", "4:1"], history);
    }

    [Theory]
    [InlineData(0, ExecutionCapacityStatus.Available)]
    [InlineData(1, ExecutionCapacityStatus.AtCapacity)]
    [InlineData(2, ExecutionCapacityStatus.AtCapacity)]
    public async Task The_status_follows_the_persisted_limit_not_a_fixed_two(int owners, ExecutionCapacityStatus expected)
    {
        await ExecutionCapacitySeed.SetLimitAsync(fixture, 1);
        for (var index = 0; index < owners; index++)
        {
            await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Agent);
        }

        var reading = await ReadAsync();

        Assert.Equal(expected, reading.Status);
        Assert.Equal(owners, reading.RecordedOwners);
        Assert.Equal(1, reading.Limit);
    }

    [Fact]
    public async Task A_tracked_configuration_never_hides_a_newer_persisted_revision()
    {
        await using var longLived = fixture.CreateContext();
        var tracked = await longLived.ExecutionCapacityConfigurations.SingleAsync();
        Assert.Equal(2, tracked.Limit);

        await ExecutionCapacitySeed.SetLimitAsync(fixture, 1);
        var reading = await ExecutionCapacityPolicy.ReadAsync(longLived, null, CancellationToken.None);

        Assert.Equal(1, reading.Limit);
        Assert.Equal(2, reading.Revision);
    }

    [Fact]
    public async Task A_missing_configuration_is_unavailable_without_a_limit_and_is_never_reseeded()
    {
        await using (var db = fixture.CreateContext())
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM execution_capacity_configurations");
        }

        var reading = await ReadAsync();

        Assert.Equal(new ExecutionCapacityReading(ExecutionCapacityStatus.Unavailable, null, null, null), reading);
        await using var verify = fixture.CreateContext();
        Assert.Empty(await verify.ExecutionCapacityConfigurations.AsNoTracking().ToListAsync());
    }

    [Theory]
    [InlineData(2L, 3L)]
    [InlineData(2L, 0L)]
    [InlineData(2L, -1L)]
    [InlineData(2L, 1.5)]
    [InlineData(2L, 2.5)]
    [InlineData(2L, "abc")]
    [InlineData(2L, new byte[] { 0x31 })]
    public async Task A_malformed_latest_revision_is_unavailable_and_never_falls_back_to_older_history(object revision, object limit)
    {
        await RawConfigurationRows.InsertAsync(fixture.ConnectionString, revision, limit, true);
        Assert.Equal(2, await RowCountAsync());

        var reading = await ReadAsync();

        Assert.Equal(new ExecutionCapacityReading(ExecutionCapacityStatus.Unavailable, null, null, null), reading);
    }

    [Fact]
    public async Task A_revision_at_the_largest_integer_is_still_a_valid_current_setting()
    {
        await RawConfigurationRows.InsertAsync(fixture.ConnectionString, long.MaxValue, 1L, true);

        var reading = await ReadAsync();

        Assert.Equal(1, reading.Limit);
        Assert.Equal(long.MaxValue, reading.Revision);
    }

    [Fact]
    public async Task An_untrustworthy_ownership_reading_keeps_the_validated_limit_and_states_no_count()
    {
        await ExecutionCapacitySeed.SetLimitAsync(fixture, 1);
        var other = await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Agent);
        await using (var db = fixture.CreateContext())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE attempts SET AgentRole = {"CodeReviewer"} WHERE Id = {other.Id}");
        }

        var reading = await ReadAsync();

        Assert.Equal(new ExecutionCapacityReading(ExecutionCapacityStatus.Unavailable, null, 1, 2), reading);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(3L)]
    [InlineData(-1L)]
    [InlineData(1.5)]
    [InlineData("x")]
    public async Task The_stored_shape_is_enforced_by_the_database_constraints(object limit)
    {
        var refusal = await Assert.ThrowsAsync<SqliteException>(
            () => RawConfigurationRows.InsertAsync(fixture.ConnectionString, 2L, limit, false));

        Assert.Contains("CHECK constraint failed", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(1, await RowCountAsync());
    }

    private async Task<int> RowCountAsync()
    {
        await using var db = fixture.CreateContext();
        return await db.ExecutionCapacityConfigurations.AsNoTracking().CountAsync();
    }
}
