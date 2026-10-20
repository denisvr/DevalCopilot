using DevalCopilot.Application.Features.Runs.Policies.ExecutionCapacity;
using DevalCopilot.Application.Features.Runs.Queries.GetExecutionCapacity;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>ADR-0058's closed tuples through the real query handler: limit 2 always; Available counts 0 or 1 with no reason; AtCapacity
/// counts 2 or more with <c>execution_capacity_limit_reached</c>; Unavailable has no count and <c>execution_capacity_unavailable</c>. The
/// read writes nothing and a database fault is not turned into Available.</summary>
public sealed class GetExecutionCapacityQueryHandlerTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<GetExecutionCapacityQueryResult> QueryAsync()
    {
        await using var db = _fixture.CreateContext();
        var result = await new GetExecutionCapacityQueryHandler(db).HandleAsync(new GetExecutionCapacityQuery(), CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Zero_or_one_owner_is_Available_with_no_reason(int owners)
    {
        for (var index = 0; index < owners; index++)
        {
            await ExecutionCapacitySeed.AddAsync(_fixture, OwnerFamily.Agent);
        }

        var result = await QueryAsync();

        Assert.Equal(new GetExecutionCapacityQueryResult(ExecutionCapacityStatus.Available, 2, owners, null), result);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Two_or_more_owners_are_AtCapacity_with_the_fixed_reason(int owners)
    {
        var families = new[] { OwnerFamily.Agent, OwnerFamily.Verification, OwnerFamily.LocalCommit };
        for (var index = 0; index < owners; index++)
        {
            await ExecutionCapacitySeed.AddAsync(_fixture, families[index]);
        }

        var result = await QueryAsync();

        Assert.Equal(
            new GetExecutionCapacityQueryResult(ExecutionCapacityStatus.AtCapacity, 2, owners, "execution_capacity_limit_reached"), result);
    }

    [Fact]
    public async Task Unsafe_ownership_is_Unavailable_with_no_count_never_a_fabricated_one()
    {
        var owner = await ExecutionCapacitySeed.AddAsync(_fixture, OwnerFamily.Agent);
        await using (var db = _fixture.CreateContext())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE attempts SET AgentResponseContract = NULL WHERE Id = {owner.Id}");
        }

        var result = await QueryAsync();

        Assert.Equal(
            new GetExecutionCapacityQueryResult(ExecutionCapacityStatus.Unavailable, 2, null, "execution_capacity_unavailable"), result);
    }

    [Fact]
    public async Task A_read_changes_no_recorded_fact()
    {
        await ExecutionCapacitySeed.AddAsync(_fixture, OwnerFamily.Agent, OwnerFamily.Verification, OwnerFamily.LocalCommit);
        await using var before = _fixture.CreateContext();
        var states = await before.Attempts.CountAsync() + await before.VerificationExecutions.CountAsync()
            + await before.LocalCommitOperations.CountAsync() + await before.Events.CountAsync();

        await QueryAsync();
        await QueryAsync();

        await using var after = _fixture.CreateContext();
        Assert.Equal(
            states,
            await after.Attempts.CountAsync() + await after.VerificationExecutions.CountAsync()
            + await after.LocalCommitOperations.CountAsync() + await after.Events.CountAsync());
    }
}
