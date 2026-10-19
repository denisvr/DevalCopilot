using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.AgentSupervisions;

/// <summary>ADR-0056 registration: one observation per claimed Running Agent attempt of any of the seven contracts, identical replay,
/// no competing identity, and refusal for everything that is not an observable attempt. Registration never changes the attempt.</summary>
public sealed class AgentSupervisionRegistrationTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    public static TheoryData<AgentResponseContract> Contracts() =>
    [
        AgentResponseContract.Proposal,
        AgentResponseContract.CriticalReview,
        AgentResponseContract.ChallengeResolution,
        AgentResponseContract.ImplementationReport,
        AgentResponseContract.ImplementationReview,
        AgentResponseContract.ReviewCorrection,
        AgentResponseContract.VerificationDiagnosis,
    ];

    [Theory]
    [MemberData(nameof(Contracts))]
    public async Task Every_existing_agent_contract_registers_in_Preparing_with_a_two_minute_deadline(AgentResponseContract contract)
    {
        var scene = await AgentSupervisionTestScene.CreateAsync(fixture);
        var attemptId = await scene.ClaimAsync(contract);
        var observer = Guid.NewGuid();

        var result = await scene.RegisterAsync(attemptId, observer);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
        Assert.False(result.Value.Replayed);
        var receipt = await scene.ReceiptAsync(attemptId);
        Assert.Equal(result.Value.SupervisionId, receipt.Id);
        Assert.Equal((scene.RunId, attemptId, scene.Host.Id, observer), (receipt.RunId, receipt.AttemptId, receipt.HostInstanceId, receipt.ObserverId));
        Assert.Equal(AgentSupervisionPhase.Preparing, receipt.Phase);
        Assert.Equal(1, receipt.Revision);
        Assert.Equal(AgentSupervisionTestScene.Start, receipt.StartedAtUtc);
        Assert.Equal(AgentSupervisionTestScene.Start, receipt.LastHeartbeatAtUtc);
        Assert.Equal(AgentSupervisionTestScene.Start.AddMinutes(2), receipt.PhaseDeadlineUtc);
        Assert.Null(receipt.AttentionReason);
        Assert.Null(receipt.ClosedAtUtc);
    }

    [Fact]
    public async Task Registration_changes_nothing_but_the_receipt_and_its_one_registered_event()
    {
        var scene = await AgentSupervisionTestScene.CreateAsync(fixture);
        var attemptId = await scene.ClaimAsync();
        var factsBefore = await scene.FactsAsync();
        var attemptBefore = await scene.AttemptFactsAsync(attemptId);

        await scene.ObserveAttemptAsync(attemptId);

        Assert.Equal(factsBefore with { Events = factsBefore.Events + 1 }, await scene.FactsAsync());
        Assert.Equal(attemptBefore, await scene.AttemptFactsAsync(attemptId));
        Assert.Equal([RunEventType.AgentSupervisionRegistered], await scene.SupervisionEventTypesAsync());
    }

    [Fact]
    public async Task An_identical_registration_replays_and_never_duplicates_or_advances_the_receipt()
    {
        var scene = await AgentSupervisionTestScene.CreateAsync(fixture);
        var attemptId = await scene.ClaimAsync();
        var observer = Guid.NewGuid();
        var first = await scene.RegisterAsync(attemptId, observer);
        scene.Clock.Advance(TimeSpan.FromSeconds(20));

        var replay = await scene.RegisterAsync(attemptId, observer);

        Assert.True(replay.IsSuccess);
        Assert.True(replay.Value.Replayed);
        Assert.Equal(first.Value.SupervisionId, replay.Value.SupervisionId);
        Assert.Equal(1, replay.Value.Revision);
        await using var context = fixture.CreateContext();
        Assert.Equal(1, await context.AgentSupervisions.CountAsync());
        Assert.Equal(AgentSupervisionTestScene.Start, (await scene.ReceiptAsync(attemptId)).LastHeartbeatAtUtc);
    }

    [Fact]
    public async Task A_competing_observer_never_replaces_or_adopts_the_existing_observation()
    {
        var scene = await AgentSupervisionTestScene.CreateAsync(fixture);
        var attemptId = await scene.ClaimAsync();
        var owner = await scene.ObserveAttemptAsync(attemptId);
        var before = await scene.ReceiptAsync(attemptId);

        var competing = await scene.RegisterAsync(attemptId, Guid.NewGuid());

        Assert.True(competing.IsFailure);
        Assert.Equal(AgentSupervisionErrors.ConflictCode, competing.Errors[0].Code);
        var after = await scene.ReceiptAsync(attemptId);
        Assert.Equal(owner, after.ObserverId);
        Assert.Equal((before.Id, before.Revision, before.StartedAtUtc), (after.Id, after.Revision, after.StartedAtUtc));
    }

    [Fact]
    public async Task Only_the_running_host_may_register_so_another_launch_identity_is_refused_and_nothing_is_written()
    {
        var scene = await AgentSupervisionTestScene.CreateAsync(fixture);
        var attemptId = await scene.ClaimAsync();

        var result = await scene.RegisterAsync(attemptId, Guid.NewGuid(), hostInstanceId: Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(AgentSupervisionErrors.NotCurrentHostCode, result.Errors[0].Code);
        await using var context = fixture.CreateContext();
        Assert.Empty(await context.AgentSupervisions.ToListAsync());
    }

    [Theory]
    [InlineData(AttemptStatus.Completed)]
    [InlineData(AttemptStatus.Failed)]
    [InlineData(AttemptStatus.Interrupted)]
    public async Task A_terminal_attempt_is_never_registered(AttemptStatus status)
    {
        var scene = await AgentSupervisionTestScene.CreateAsync(fixture);
        var attemptId = await scene.ClaimAsync();
        await scene.CompleteAsync(attemptId, status);

        var result = await scene.RegisterAsync(attemptId, Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(AgentSupervisionErrors.AttemptNotObservableCode, result.Errors[0].Code);
    }

    [Fact]
    public async Task A_simulated_attempt_unknown_attempt_and_foreign_run_are_never_registered()
    {
        var scene = await AgentSupervisionTestScene.CreateAsync(fixture);
        var other = await AgentSupervisionTestScene.CreateAsync(fixture);
        var simulated = await scene.ClaimSimulatedAsync();

        // The simulated attempt is still Running: only its kind makes it unobservable.
        var simulatedResult = await scene.RegisterAsync(simulated, Guid.NewGuid());
        Assert.True(simulatedResult.IsFailure);
        Assert.Equal(AgentSupervisionErrors.AttemptNotObservableCode, simulatedResult.Errors[0].Code);

        await scene.CompleteAsync(simulated);
        var agent = await scene.ClaimAsync();

        foreach (var (attemptId, runId) in new[] { (Guid.NewGuid(), scene.RunId), (agent, other.RunId), (agent, Guid.NewGuid()) })
        {
            var result = await scene.RegisterAsync(attemptId, Guid.NewGuid(), runId: runId);
            Assert.True(result.IsFailure);
            Assert.Equal(AgentSupervisionErrors.AttemptNotObservableCode, result.Errors[0].Code);
        }

        await using var context = fixture.CreateContext();
        Assert.Empty(await context.AgentSupervisions.ToListAsync());
    }

    [Fact]
    public async Task An_attempt_whose_contract_and_role_disagree_is_not_observable()
    {
        var scene = await AgentSupervisionTestScene.CreateAsync(fixture);
        var attemptId = await scene.ClaimAsync();
        await using (var context = fixture.CreateContext())
        {
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE attempts SET \"AgentRole\" = 'Resolver' WHERE \"Id\" = {0}", attemptId.ToString().ToUpperInvariant());
        }

        var result = await scene.RegisterAsync(attemptId, Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(AgentSupervisionErrors.AttemptNotObservableCode, result.Errors[0].Code);
    }

    [Fact]
    public async Task Empty_identifiers_are_refused_before_any_write()
    {
        var scene = await AgentSupervisionTestScene.CreateAsync(fixture);
        var attemptId = await scene.ClaimAsync();

        var result = await scene.RegisterAsync(attemptId, Guid.Empty);

        Assert.True(result.IsFailure);
        Assert.Equal(AgentSupervisionErrors.InvalidRequestCode, result.Errors[0].Code);
    }

    [Fact]
    public async Task An_unrepresentable_clock_is_refused_never_fabricated_as_a_fresh_heartbeat()
    {
        var scene = await AgentSupervisionTestScene.CreateAsync(fixture);
        var attemptId = await scene.ClaimAsync();
        scene.Clock.Advance(DateTimeOffset.MaxValue.AddSeconds(-1) - AgentSupervisionTestScene.Start);

        var result = await scene.RegisterAsync(attemptId, Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(AgentSupervisionErrors.ChronologyInvalidCode, result.Errors[0].Code);
        await using var context = fixture.CreateContext();
        Assert.Empty(await context.AgentSupervisions.ToListAsync());
    }

    [Fact]
    public async Task Two_independent_connections_racing_to_register_leave_exactly_one_owner()
    {
        var scene = await AgentSupervisionTestScene.CreateAsync(fixture);
        var attemptId = await scene.ClaimAsync();
        var observers = Enumerable.Range(0, 8).Select(_ => Guid.NewGuid()).ToArray();

        var results = await Task.WhenAll(observers.Select(observer => Task.Run(() => scene.RegisterAsync(attemptId, observer))));

        var winners = results.Where(result => result.IsSuccess && !result.Value.Replayed).ToList();
        Assert.Single(winners);
        var receipt = await scene.ReceiptAsync(attemptId);
        Assert.Contains(receipt.ObserverId, observers);
        Assert.Equal(1, receipt.Revision);
        Assert.All(
            results.Where(result => result.IsFailure),
            result => Assert.Contains(
                result.Errors[0].Code, new[] { AgentSupervisionErrors.ConflictCode, AgentSupervisionErrors.PersistenceFailedCode }));
    }
}
