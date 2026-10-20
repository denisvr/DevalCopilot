using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// GAD-R1 (ADR-0058): the single-use Agent dispatch gate never trusts a stale tracked classification. The handler may have loaded an
/// attempt before another connection changed its stored role and response contract; the capacity exemption (and every role-specific
/// branch) must rest on the pair that is persisted now, so a tracked/current pair mismatch is refused with the existing fixed
/// <c>agent_attempts.invalid_agent_contract</c> Conflict, whatever the occupancy, before anything can be marked. The refusal leaves the
/// pending tracked state untouched and commits no dispatch marker and no event. Only a currently coherent ReadOnly pair is exempt.
/// </summary>
public sealed class AgentDispatchStaleClassificationTests : IAsyncLifetime
{
    private const string InvalidContract = "agent_attempts.invalid_agent_contract";
    private const string LimitReached = "execution_capacity.limit_reached";
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private sealed record Tuple(
        AgentResponseContract? Contract, AgentRole? Role, AgentProvider? Provider, AgentPermissionProfile? Profile, string? Version);

    private async Task<Tuple> StoredTupleAsync(Guid attemptId)
    {
        await using var db = _fixture.CreateContext();
        return await db.Attempts.AsNoTracking().Where(row => row.Id == attemptId).Select(row => new Tuple(
            row.AgentResponseContract, row.AgentRole, row.AgentProvider, row.AgentPermissionProfile, row.AgentAdapterContractVersion))
            .SingleAsync();
    }

    /// <summary>Changes the stored assignment through another connection, exactly as an out-of-band writer would.</summary>
    private async Task RestoreTupleAsync(Guid attemptId, Tuple tuple)
    {
        await using var concurrent = _fixture.CreateContext();
        await concurrent.Attempts.Where(row => row.Id == attemptId).ExecuteUpdateAsync(set => set
            .SetProperty(row => row.AgentResponseContract, tuple.Contract)
            .SetProperty(row => row.AgentRole, tuple.Role)
            .SetProperty(row => row.AgentProvider, tuple.Provider)
            .SetProperty(row => row.AgentPermissionProfile, tuple.Profile)
            .SetProperty(row => row.AgentAdapterContractVersion, tuple.Version));
    }

    private Task<(Guid RunId, Guid AttemptId)> SeedAsync(string path) =>
        AgentAttemptModeSeed.SeedClaimedAsync(_fixture, path, AbandonmentRaceSupport.ManualAgentMode);

    private async Task<Devalente.Shared.Results.Result<DateTimeOffset>> DispatchAsync(DevalCopilotDbContext db, Guid runId, Guid attemptId)
    {
        await using var transaction = await db.BeginTransactionAsync(CancellationToken.None);
        var result = await new MarkAgentAttemptDispatchedCommandHandler(db, new FixedTimeProvider(Now))
            .HandleAsync(new MarkAgentAttemptDispatchedCommand(runId, attemptId), CancellationToken.None);
        if (result.IsFailure)
        {
            await transaction.RollbackAsync();
            return result;
        }

        await db.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync();
        return result;
    }

    private async Task<int> EventCountAsync()
    {
        await using var verify = _fixture.CreateContext();
        return await verify.Events.AsNoTracking().CountAsync();
    }

    private async Task AssertNothingCommittedAsync(Guid attemptId, Tuple storedBefore, int eventsBefore)
    {
        await using var verify = _fixture.CreateContext();
        var stored = await verify.Attempts.AsNoTracking().SingleAsync(row => row.Id == attemptId);
        Assert.Null(stored.AgentDispatchedAtUtc);
        Assert.Equal(AttemptStatus.Running, stored.Status);
        Assert.Null(stored.AgentOutcome);
        Assert.Equal(storedBefore, await StoredTupleAsync(attemptId));
        Assert.Equal(eventsBefore, await EventCountAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_stale_ReadOnly_tracked_pair_over_a_current_mutating_pair_is_refused_at_any_occupancy(bool fullCapacity)
    {
        var (runId, attemptId) = await SeedAsync("planning");
        var (_, mutatingId) = await SeedAsync("implementation");
        var mutating = await StoredTupleAsync(mutatingId);
        await using var handlerDb = _fixture.CreateContext();
        var tracked = await handlerDb.Attempts.SingleAsync(row => row.Id == attemptId);
        Assert.Equal(AgentResponseContract.Proposal, tracked.AgentResponseContract);
        await RestoreTupleAsync(attemptId, mutating);
        await using (var db = _fixture.CreateContext())
        {
            // The donor attempt ended so only the converted row and the planted owners are recorded.
            await db.Attempts.Where(row => row.Id == mutatingId)
                .ExecuteUpdateAsync(set => set.SetProperty(row => row.Status, AttemptStatus.Interrupted));
        }

        if (fullCapacity)
        {
            await ExecutionCapacitySeed.AddAsync(_fixture, OwnerFamily.Verification, OwnerFamily.Verification);
        }

        var storedBefore = await StoredTupleAsync(attemptId);
        var eventsBefore = await EventCountAsync();

        var result = await DispatchAsync(handlerDb, runId, attemptId);

        Assert.True(result.IsFailure, "A stale ReadOnly tracked pair dispatched a stored mutating attempt.");
        Assert.Equal(InvalidContract, Assert.Single(result.Errors).Code);
        Assert.Equal(AgentResponseContract.Proposal, tracked.AgentResponseContract);
        Assert.Null(tracked.AgentDispatchedAtUtc);
        await AssertNothingCommittedAsync(attemptId, storedBefore, eventsBefore);
    }

    [Fact]
    public async Task A_stale_mutating_tracked_pair_over_a_current_ReadOnly_pair_is_refused_and_pending_tracking_is_untouched()
    {
        var (runId, attemptId) = await SeedAsync("implementation");
        var (_, readOnlyId) = await SeedAsync("planning");
        var readOnly = await StoredTupleAsync(readOnlyId);
        await using var handlerDb = _fixture.CreateContext();
        var tracked = await handlerDb.Attempts.SingleAsync(row => row.Id == attemptId);
        await RestoreTupleAsync(attemptId, readOnly);
        var storedBefore = await StoredTupleAsync(attemptId);
        var eventsBefore = await EventCountAsync();

        var result = await DispatchAsync(handlerDb, runId, attemptId);

        Assert.True(result.IsFailure);
        Assert.Equal(InvalidContract, Assert.Single(result.Errors).Code);
        Assert.Equal(AgentResponseContract.ImplementationReport, tracked.AgentResponseContract);
        Assert.Equal(AgentRole.Implementer, tracked.AgentRole);
        Assert.Null(tracked.AgentDispatchedAtUtc);
        await AssertNothingCommittedAsync(attemptId, storedBefore, eventsBefore);
    }

    [Fact]
    public async Task A_stale_tracked_role_alone_is_a_pair_mismatch_even_when_the_contract_is_unchanged()
    {
        var (runId, attemptId) = await SeedAsync("planning");
        await using var handlerDb = _fixture.CreateContext();
        await handlerDb.Attempts.SingleAsync(row => row.Id == attemptId);
        await using (var concurrent = _fixture.CreateContext())
        {
            await concurrent.Attempts.Where(row => row.Id == attemptId)
                .ExecuteUpdateAsync(set => set.SetProperty(row => row.AgentRole, AgentRole.Implementer));
        }

        var storedBefore = await StoredTupleAsync(attemptId);
        var eventsBefore = await EventCountAsync();

        var result = await DispatchAsync(handlerDb, runId, attemptId);

        Assert.True(result.IsFailure);
        Assert.Equal(InvalidContract, Assert.Single(result.Errors).Code);
        await AssertNothingCommittedAsync(attemptId, storedBefore, eventsBefore);
    }

    [Fact]
    public async Task A_currently_coherent_ReadOnly_pair_dispatches_at_full_capacity()
    {
        var (runId, attemptId) = await SeedAsync("planning");
        await ExecutionCapacitySeed.AddAsync(_fixture, OwnerFamily.Verification, OwnerFamily.Verification);
        await using var handlerDb = _fixture.CreateContext();

        var result = await DispatchAsync(handlerDb, runId, attemptId);

        Assert.True(result.IsSuccess);
        await using var verify = _fixture.CreateContext();
        Assert.NotNull((await verify.Attempts.AsNoTracking().SingleAsync(row => row.Id == attemptId)).AgentDispatchedAtUtc);
    }

    [Fact]
    public async Task A_currently_coherent_mutating_pair_still_judges_capacity_and_excludes_only_itself()
    {
        var (runId, attemptId) = await SeedAsync("implementation");
        await ExecutionCapacitySeed.AddAsync(_fixture, OwnerFamily.Verification);
        await using (var admitted = _fixture.CreateContext())
        {
            Assert.True((await DispatchAsync(admitted, runId, attemptId)).IsSuccess);
        }

        var (secondRun, second) = await SeedAsync("review-correction");
        await using var refused = _fixture.CreateContext();
        var result = await DispatchAsync(refused, secondRun, second);

        // Beside the dispatched implementation and one verification owner, a third mutating owner cannot dispatch.
        Assert.True(result.IsFailure);
        Assert.Equal(LimitReached, Assert.Single(result.Errors).Code);
    }
}
