using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.CreateManualRun;
using DevalCopilot.Application.Features.Runs.Commands.StartSimulatedRun;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>ADR-0028: a new manual run may carry smaller, immutable, run-wide claim and reserved-time ceilings chosen at
/// intake, persisted in the existing columns by the same atomic creation; simulated creation keeps the fixed defaults.</summary>
public sealed class ManualRunBudgetCreationTests(SqliteDatabaseFixture fixture) : IClassFixture<SqliteDatabaseFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 10, 0, 0, TimeSpan.Zero);

    private async Task<Guid> SeedProjectAsync()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "Budgets", $@"C:\repos\{Guid.NewGuid():N}", Now);
        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        return project.Id;
    }

    private async Task<Guid> CreateAsync(Guid projectId, int? attempts, int? minutes)
    {
        await using var dbContext = fixture.CreateContext();
        var result = await new CreateManualRunCommandHandler(dbContext, new FixedTimeProvider(Now)).HandleAsync(
            new CreateManualRunCommand(projectId, "Budgeted objective", attempts, minutes), CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value.RunId;
    }

    private async Task<(int Attempts, TimeSpan? Time)> ReadBudgetsAsync(Guid runId)
    {
        await using var dbContext = fixture.CreateContext();
        var run = await dbContext.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == runId);
        return (run.MaximumAgentAttempts, run.MaximumAgentInvocationTime);
    }

    [Theory]
    [InlineData(null, null, 16, 120)]
    [InlineData(4, null, 4, 120)]
    [InlineData(null, 40, 16, 40)]
    [InlineData(4, 40, 4, 40)]
    [InlineData(1, 1, 1, 1)]
    [InlineData(16, 120, 16, 120)]
    public async Task Each_budget_is_chosen_independently_and_persisted_exactly(
        int? attempts, int? minutes, int expectedAttempts, int expectedMinutes)
    {
        var runId = await CreateAsync(await SeedProjectAsync(), attempts, minutes);

        var persisted = await ReadBudgetsAsync(runId);

        Assert.Equal(expectedAttempts, persisted.Attempts);
        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), persisted.Time);
    }

    [Fact]
    public async Task Persisted_budgets_never_change_across_later_saves_of_the_same_run()
    {
        var runId = await CreateAsync(await SeedProjectAsync(), 3, 25);

        await using (var dbContext = fixture.CreateContext())
        {
            var run = await dbContext.Runs.SingleAsync(candidate => candidate.Id == runId);
            run.Claim(Now);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        Assert.Equal((3, TimeSpan.FromMinutes(25)), await ReadBudgetsAsync(runId));
    }

    [Fact]
    public async Task Simulated_creation_keeps_the_fixed_defaults()
    {
        var projectId = await SeedProjectAsync();
        await using var dbContext = fixture.CreateContext();

        var result = await new StartSimulatedRunCommandHandler(dbContext, new FixedTimeProvider(Now))
            .HandleAsync(new StartSimulatedRunCommand(projectId, "Demo"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal((16, TimeSpan.FromMinutes(120)), await ReadBudgetsAsync(result.Value.RunId));
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData(1, 1, true)]
    [InlineData(16, 120, true)]
    [InlineData(0, null, false)]
    [InlineData(-1, null, false)]
    [InlineData(17, null, false)]
    [InlineData(int.MaxValue, null, false)]
    [InlineData(int.MinValue, null, false)]
    [InlineData(null, 0, false)]
    [InlineData(null, -1, false)]
    [InlineData(null, 121, false)]
    [InlineData(null, int.MaxValue, false)]
    [InlineData(null, int.MinValue, false)]
    [InlineData(4, 0, false)]
    [InlineData(17, 40, false)]
    public void The_transport_range_is_one_to_sixteen_claims_and_one_to_one_hundred_twenty_whole_minutes(
        int? attempts, int? minutes, bool valid)
    {
        var result = new CreateManualRunCommandValidator()
            .Validate(new CreateManualRunCommand(Guid.NewGuid(), "Objective", attempts, minutes));

        Assert.Equal(valid, result.IsValid);
        if (!valid)
        {
            Assert.All(result.Errors, error => Assert.Equal("validation.invalid", error.ErrorCode));
        }
    }

    // A competing creation committed between this request's admission read and its save rolls the whole loser back,
    // including its chosen budgets; the winner's own budgets stay exactly as it chose them.
    [Fact]
    public async Task A_losing_budgeted_creation_leaves_no_run_event_or_counter_advance_and_the_winner_keeps_its_budgets()
    {
        var projectId = await SeedProjectAsync();
        await using var inner = fixture.CreateContext();
        var winnerRunId = Guid.Empty;
        var faulting = new FaultInjectingDbContext(inner)
        {
            BeforeSaveChanges = async _ => winnerRunId = await CreateAsync(projectId, 2, 15),
        };

        var result = await new CreateManualRunCommandHandler(faulting, new FixedTimeProvider(Now))
            .HandleAsync(new CreateManualRunCommand(projectId, "Loser", 9, 90), CancellationToken.None);

        Assert.Equal(RunIntentRecorder.ConcurrentIntentCode, Assert.Single(result.Errors).Code);
        await using var verify = fixture.CreateContext();
        var run = Assert.Single(verify.Runs.Where(candidate => candidate.ProjectId == projectId));
        Assert.Equal(winnerRunId, run.Id);
        Assert.Equal((2, (TimeSpan?)TimeSpan.FromMinutes(15)), (run.MaximumAgentAttempts, run.MaximumAgentInvocationTime));
        Assert.Single(verify.Events.Where(runEvent => runEvent.RunId == run.Id));
        Assert.Equal(2, verify.Projects.Single(project => project.Id == projectId).NextExecutionNumber);
    }

    [Fact]
    public async Task A_blocked_budgeted_creation_writes_nothing()
    {
        var projectId = await SeedProjectAsync();
        await CreateAsync(projectId, 5, 50);
        await using var dbContext = fixture.CreateContext();

        var result = await new CreateManualRunCommandHandler(dbContext, new FixedTimeProvider(Now))
            .HandleAsync(new CreateManualRunCommand(projectId, "Second", 1, 1), CancellationToken.None);

        Assert.Equal(RunIntentRecorder.BlockedCode, Assert.Single(result.Errors).Code);
        await using var verify = fixture.CreateContext();
        var run = Assert.Single(verify.Runs.Where(candidate => candidate.ProjectId == projectId));
        Assert.Equal((5, (TimeSpan?)TimeSpan.FromMinutes(50)), (run.MaximumAgentAttempts, run.MaximumAgentInvocationTime));
        Assert.Equal(2, verify.Projects.Single(project => project.Id == projectId).NextExecutionNumber);
    }

    // The claim-path suites seed their runs with the Domain factory and these exact ceilings. This bridge proves the production
    // creation stores a row indistinguishable from that factory's, so what those suites prove about non-default persisted
    // ceilings is what a manual run created through the operation receives.
    [Fact]
    public async Task A_production_created_run_is_indistinguishable_from_the_domain_factory_run_with_the_same_ceilings()
    {
        var projectId = await SeedProjectAsync();
        var runId = await CreateAsync(projectId, 4, 40);

        await using var dbContext = fixture.CreateContext();
        var stored = await dbContext.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == runId);
        var factory = Run.RecordClassifiedIntent(
            stored.Id, projectId, stored.ExecutionNumber, RunExecutionMode.ManualAgent, stored.Objective, Now,
            maximumAgentAttempts: 4, maximumAgentInvocationTime: TimeSpan.FromMinutes(40));

        Dictionary<string, object?> Snapshot(Run run) => dbContext.Entry(run).Properties
            .ToDictionary(property => property.Metadata.Name, property => property.CurrentValue);
        var storedSnapshot = Snapshot(stored);
        var factorySnapshot = Snapshot(factory);

        Assert.Equal(factorySnapshot.OrderBy(pair => pair.Key), storedSnapshot.OrderBy(pair => pair.Key));
        Assert.Equal(4, stored.MaximumAgentAttempts);
        Assert.Equal(TimeSpan.FromMinutes(40), stored.MaximumAgentInvocationTime);
    }
}
