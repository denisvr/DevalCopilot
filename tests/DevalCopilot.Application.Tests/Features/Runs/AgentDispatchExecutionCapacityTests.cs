using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// ADR-0058 at the single-use Agent dispatch marker, the last gate before a provider is invoked. The gate runs inside its own write-locked
/// transaction (the production pipeline's, reproduced here around the handler). Only a potentially mutating attempt consumes capacity: it
/// excludes exactly its own coherent row of its own table, so it may dispatch beside one other recorded owner and is withheld when two
/// others are recorded anywhere. A refusal commits no marker, rewrites no recorded fact and terminalizes nothing; a recognized ReadOnly
/// attempt dispatches at any occupancy; and a genuine terminal transition of another owner frees the place.
/// </summary>
public sealed class AgentDispatchExecutionCapacityTests : IAsyncLifetime
{
    private const string LimitReached = "execution_capacity.limit_reached";
    private const string Unavailable = "execution_capacity.unavailable";
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 13, 0, 0, TimeSpan.Zero);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<Devalente.Shared.Results.Result<MarkAgentAttemptDispatchedCommandResult>> DispatchAsync(Guid runId, Guid attemptId)
    {
        await using var db = _fixture.CreateContext();
        return await DispatchAsync(db, runId, attemptId);
    }

    private static async Task<Devalente.Shared.Results.Result<MarkAgentAttemptDispatchedCommandResult>> DispatchAsync(DevalCopilotDbContext db, Guid runId, Guid attemptId)
    {
        // The EF transaction behavior of every ICommand: the lock is taken before the handler reads, the save and commit follow success.
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

    private async Task AssertUndispatchedAsync(Guid attemptId)
    {
        await using var verify = _fixture.CreateContext();
        var attempt = await verify.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attemptId);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Null(attempt.AgentDispatchedAtUtc);
        Assert.Null(attempt.AgentOutcome);
    }

    private async Task AssertDispatchedAsync(Guid attemptId)
    {
        await using var verify = _fixture.CreateContext();
        Assert.NotNull((await verify.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attemptId)).AgentDispatchedAtUtc);
    }

    private Task<(Guid RunId, Guid AttemptId)> SeedAsync(string path) =>
        AgentAttemptModeSeed.SeedClaimedAsync(_fixture, path, AbandonmentRaceSupport.ManualAgentMode);

    public static TheoryData<string> MutatingPaths() => ["implementation", "review-correction"];

    public static TheoryData<string> ReadOnlyPaths() => ["planning", "critical-review", "challenge-resolution", "code-review"];

    [Theory]
    [MemberData(nameof(MutatingPaths))]
    public async Task The_only_recorded_owner_dispatches(string path)
    {
        var (runId, attemptId) = await SeedAsync(path);

        Assert.True((await DispatchAsync(runId, attemptId)).IsSuccess);
        await AssertDispatchedAsync(attemptId);
    }

    [Theory]
    [MemberData(nameof(MutatingPaths))]
    public async Task Two_admitted_owners_may_both_dispatch(string path)
    {
        var (firstRun, first) = await SeedAsync(path);
        var (secondRun, second) = await SeedAsync("implementation");

        Assert.True((await DispatchAsync(firstRun, first)).IsSuccess);
        Assert.True((await DispatchAsync(secondRun, second)).IsSuccess);
        await AssertDispatchedAsync(first);
        await AssertDispatchedAsync(second);
    }

    [Theory]
    [InlineData("implementation", OwnerFamily.Agent)]
    [InlineData("implementation", OwnerFamily.Verification)]
    [InlineData("implementation", OwnerFamily.LocalCommit)]
    [InlineData("review-correction", OwnerFamily.Agent)]
    [InlineData("review-correction", OwnerFamily.Verification)]
    [InlineData("review-correction", OwnerFamily.LocalCommit)]
    public async Task Two_other_recorded_owners_anywhere_withhold_the_dispatch_and_leave_every_fact_as_recorded(string path, OwnerFamily third)
    {
        var (runId, attemptId) = await SeedAsync(path);
        await SeedAsync("implementation");
        await ExecutionCapacitySeed.AddAsync(_fixture, third);
        var before = await FactsAsync();

        var result = await DispatchAsync(runId, attemptId);

        Assert.True(result.IsFailure);
        Assert.Equal(LimitReached, Assert.Single(result.Errors).Code);
        Assert.NotEqual(MarkAgentAttemptDispatchedCommandHandler.WorkspaceNoLongerEligibleCode, Assert.Single(result.Errors).Code);
        await AssertUndispatchedAsync(attemptId);
        Assert.Equal(before, await FactsAsync());
    }

    [Theory]
    [MemberData(nameof(ReadOnlyPaths))]
    public async Task A_recognized_ReadOnly_attempt_dispatches_while_the_database_is_over_capacity(string path)
    {
        var (runId, attemptId) = await SeedAsync(path);
        await ExecutionCapacitySeed.AddAsync(_fixture, OwnerFamily.Agent, OwnerFamily.Verification, OwnerFamily.LocalCommit);

        Assert.True((await DispatchAsync(runId, attemptId)).IsSuccess);
        await AssertDispatchedAsync(attemptId);
    }

    [Fact]
    public async Task A_verification_execution_with_the_attempts_own_identifier_is_not_the_exact_attempt_and_is_not_excluded()
    {
        var (runId, attemptId) = await SeedAsync("implementation");
        await using (var db = _fixture.CreateContext())
        {
            var other = await ExecutionCapacitySeed.AddAsync(_fixture, OwnerFamily.Verification);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE verification_executions SET Id = {attemptId} WHERE Id = {other.Id}");
        }

        await ExecutionCapacitySeed.AddAsync(_fixture, OwnerFamily.Verification);

        // The attempt and two verification executions are recorded: two others beside the exact attempt, one of them sharing its id.
        var result = await DispatchAsync(runId, attemptId);

        Assert.Equal(LimitReached, Assert.Single(result.Errors).Code);
        await AssertUndispatchedAsync(attemptId);
    }

    [Fact]
    public async Task A_genuine_terminal_transition_of_another_owner_frees_the_place_for_a_fresh_dispatch()
    {
        var (runId, attemptId) = await SeedAsync("implementation");
        var owners = await ExecutionCapacitySeed.AddAsync(_fixture, OwnerFamily.Agent, OwnerFamily.Verification);

        Assert.Equal(LimitReached, Assert.Single((await DispatchAsync(runId, attemptId)).Errors).Code);

        await ExecutionCapacitySeed.FinishAsync(_fixture, owners[1]);

        Assert.True((await DispatchAsync(runId, attemptId)).IsSuccess);
        await AssertDispatchedAsync(attemptId);
    }

    [Fact]
    public async Task A_malformed_recorded_owner_withholds_the_dispatch_as_unavailable()
    {
        var (runId, attemptId) = await SeedAsync("implementation");
        var other = await ExecutionCapacitySeed.AddAsync(_fixture, OwnerFamily.Agent);
        await using (var db = _fixture.CreateContext())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE attempts SET AgentRole = {"CodeReviewer"} WHERE Id = {other.Id}");
        }

        var result = await DispatchAsync(runId, attemptId);

        Assert.Equal(Unavailable, Assert.Single(result.Errors).Code);
        await AssertUndispatchedAsync(attemptId);
    }

    private async Task<string> FactsAsync()
    {
        await using var db = _fixture.CreateContext();
        var attempts = await db.Attempts.AsNoTracking().Select(a => new { a.Id, a.Status, a.AgentDispatchedAtUtc }).ToListAsync();
        var executions = await db.VerificationExecutions.AsNoTracking().Select(e => new { e.Id, e.Status, e.DispatchedAtUtc }).ToListAsync();
        var operations = await db.LocalCommitOperations.AsNoTracking().Select(o => new { o.Id, o.Status }).ToListAsync();
        var events = await db.Events.CountAsync();
        return string.Join(
            ';',
            attempts.OrderBy(a => a.Id).Select(a => $"{a.Id}:{a.Status}:{a.AgentDispatchedAtUtc}")
                .Concat(executions.OrderBy(e => e.Id).Select(e => $"{e.Id}:{e.Status}:{e.DispatchedAtUtc}"))
                .Concat(operations.OrderBy(o => o.Id).Select(o => $"{o.Id}:{o.Status}"))
                .Append(events.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }
}
