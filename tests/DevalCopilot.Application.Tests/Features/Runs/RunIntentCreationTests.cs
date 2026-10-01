using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.CreateManualRun;
using DevalCopilot.Application.Features.Runs.Commands.StartSimulatedRun;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>Manual and simulated creation share one admission rule and one serialized number reservation.</summary>
public sealed class RunIntentCreationTests(SqliteDatabaseFixture fixture) : IClassFixture<SqliteDatabaseFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 10, 0, 0, TimeSpan.Zero);

    private async Task<Guid> SeedProjectAsync()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        return project.Id;
    }

    private async Task<Guid> SeedRunAsync(Guid projectId, int number, Action<Run>? transition)
    {
        await using var dbContext = fixture.CreateContext();
        var project = await dbContext.Projects.SingleAsync(candidate => candidate.Id == projectId);
        var run = Run.RecordIntent(Guid.NewGuid(), projectId, number, "Historical objective", Now);
        while (project.NextExecutionNumber <= number)
        {
            project.ReserveExecutionNumber();
        }

        transition?.Invoke(run);
        dbContext.Runs.Add(run);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        return run.Id;
    }

    private async Task<Result<CreateManualRunCommandResult>> CreateManualAsync(Guid projectId, string objective = "Plan the next increment")
    {
        await using var dbContext = fixture.CreateContext();
        return await new CreateManualRunCommandHandler(dbContext, new FixedTimeProvider(Now))
            .HandleAsync(new CreateManualRunCommand(projectId, objective), CancellationToken.None);
    }

    private async Task<Result<StartSimulatedRunCommandResult>> StartSimulatedAsync(Guid projectId, string objective = "Demo objective")
    {
        await using var dbContext = fixture.CreateContext();
        return await new StartSimulatedRunCommandHandler(dbContext, new FixedTimeProvider(Now))
            .HandleAsync(new StartSimulatedRunCommand(projectId, objective), CancellationToken.None);
    }

    private async Task<(int Runs, int Events, int NextNumber)> CountsAsync(Guid projectId)
    {
        await using var dbContext = fixture.CreateContext();
        var runIds = await dbContext.Runs.Where(run => run.ProjectId == projectId).Select(run => run.Id).ToListAsync();
        var events = await dbContext.Events.CountAsync(runEvent => runIds.Contains(runEvent.RunId));
        var next = await dbContext.Projects.Where(project => project.Id == projectId).Select(project => project.NextExecutionNumber).SingleAsync();
        return (runIds.Count, events, next);
    }

    [Fact]
    public async Task Manual_creation_persists_only_the_intent_under_the_manual_mode()
    {
        var projectId = await SeedProjectAsync();

        var result = await CreateManualAsync(projectId, "Plan the next increment");

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.ExecutionNumber);
        await using var dbContext = fixture.CreateContext();
        var run = await dbContext.Runs.SingleAsync(candidate => candidate.Id == result.Value.RunId);
        Assert.Equal(RunExecutionMode.ManualAgent, run.ExecutionMode);
        Assert.Equal(RunLifecycle.Created, run.Lifecycle);
        Assert.Equal(RunStage.Intake, run.Stage);
        Assert.Equal("Plan the next increment", run.Objective);
        Assert.Equal(2, run.MaximumReviewCorrectionAttempts);
        Assert.Equal(16, run.MaximumAgentAttempts);
        Assert.Equal(Run.DefaultMaximumAgentInvocationTime, run.MaximumAgentInvocationTime);

        var runEvent = Assert.Single(dbContext.Events, candidate => candidate.RunId == run.Id);
        Assert.Equal(RunEventType.RunStarted, runEvent.EventType);
        Assert.Equal(ParticipantIdentity.ForOrchestrator(), runEvent.Actor);

        // Nothing beyond the run and its intent event: no attempt, manifest, workspace, checkpoint, lease, or message.
        Assert.Empty(dbContext.Attempts.Where(attempt => attempt.RunId == run.Id));
        Assert.Empty(dbContext.Artifacts.Where(artifact => artifact.RunId == run.Id));
        Assert.Empty(dbContext.CollaborationMessages.Where(message => message.RunId == run.Id));
        Assert.Empty(dbContext.GitWorkspaces.Where(workspace => workspace.ProjectId == projectId));
        Assert.Empty(dbContext.RepositoryMutationLeases.Where(lease => lease.ProjectId == projectId));
        Assert.Equal(0, dbContext.Attempts.Count(attempt => attempt.RunId == run.Id && attempt.Kind == AttemptKind.Agent));
    }

    [Fact]
    public async Task Simulated_creation_assigns_the_simulated_mode()
    {
        var projectId = await SeedProjectAsync();

        var result = await StartSimulatedAsync(projectId);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, await RunExecutionModeTestSupport.ReadStoredModeAsync(fixture, result.Value.RunId));
    }

    [Fact]
    public async Task A_new_run_can_never_be_created_with_the_legacy_or_an_undefined_mode()
    {
        foreach (var mode in new[] { RunExecutionMode.Legacy, (RunExecutionMode)RunExecutionModeTestSupport.UndefinedMode })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                Run.RecordClassifiedIntent(Guid.NewGuid(), Guid.NewGuid(), 1, mode, "Objective", Now));
        }
    }

    public static TheoryData<string> TerminalLifecycles() => ["Completed", "Failed", "Interrupted"];

    [Theory]
    [MemberData(nameof(TerminalLifecycles))]
    public async Task Entirely_terminal_history_permits_both_creation_operations_with_monotonic_numbers(string terminal)
    {
        var projectId = await SeedProjectAsync();
        await SeedRunAsync(projectId, 1, run =>
        {
            run.Claim(Now);
            switch (terminal)
            {
                case "Completed": run.Complete(Now); break;
                case "Failed": run.Fail(Now); break;
                default: run.MarkInterrupted(Now); break;
            }
        });

        var manual = await CreateManualAsync(projectId);
        Assert.True(manual.IsSuccess);
        Assert.Equal(2, manual.Value.ExecutionNumber);

        // The manual run is now Created, so a simulated start is refused until it is terminal.
        var blocked = await StartSimulatedAsync(projectId);
        Assert.True(blocked.IsFailure);
        Assert.Equal(RunIntentRecorder.BlockedCode, Assert.Single(blocked.Errors).Code);
    }

    [Fact]
    public async Task Simulated_creation_after_terminal_history_takes_the_next_number()
    {
        var projectId = await SeedProjectAsync();
        await SeedRunAsync(projectId, 1, run =>
        {
            run.Claim(Now);
            run.Complete(Now);
        });
        await SeedRunAsync(projectId, 2, run =>
        {
            run.Claim(Now);
            run.Fail(Now);
        });

        var result = await StartSimulatedAsync(projectId);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.ExecutionNumber);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_created_or_running_run_blocks_both_creation_operations_and_changes_nothing(bool running)
    {
        var projectId = await SeedProjectAsync();
        await SeedRunAsync(projectId, 1, run =>
        {
            if (running)
            {
                run.Claim(Now);
            }
        });
        var before = await CountsAsync(projectId);

        var manual = await CreateManualAsync(projectId);
        var simulated = await StartSimulatedAsync(projectId);

        foreach (var error in new[] { manual.Errors, simulated.Errors })
        {
            var single = Assert.Single(error);
            Assert.Equal(RunIntentRecorder.BlockedCode, single.Code);
        }

        Assert.Equal(before, await CountsAsync(projectId));
    }

    [Fact]
    public async Task An_unrecognized_existing_lifecycle_blocks_creation()
    {
        var projectId = await SeedProjectAsync();
        var runId = await SeedRunAsync(projectId, 1, run =>
        {
            run.Claim(Now);
            run.Complete(Now);
        });
        await using (var dbContext = fixture.CreateContext())
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"UPDATE runs SET Lifecycle = 'Archived' WHERE Id = {runId}");
        }

        var before = await CountsAsync(projectId);
        var manual = await CreateManualAsync(projectId);
        var simulated = await StartSimulatedAsync(projectId);

        Assert.Equal(RunIntentRecorder.BlockedCode, Assert.Single(manual.Errors).Code);
        Assert.Equal(RunIntentRecorder.BlockedCode, Assert.Single(simulated.Errors).Code);
        Assert.Equal(before, await CountsAsync(projectId));
    }

    [Fact]
    public async Task A_database_with_several_historical_active_runs_is_preserved_and_blocks_creation()
    {
        var projectId = await SeedProjectAsync();
        await SeedRunAsync(projectId, 1, null);
        await SeedRunAsync(projectId, 2, run => run.Claim(Now));
        var before = await CountsAsync(projectId);

        var result = await CreateManualAsync(projectId);

        Assert.Equal(RunIntentRecorder.BlockedCode, Assert.Single(result.Errors).Code);
        Assert.Equal(before, await CountsAsync(projectId));
    }

    [Fact]
    public async Task Another_projects_active_run_does_not_block_creation()
    {
        var blockedProject = await SeedProjectAsync();
        await SeedRunAsync(blockedProject, 1, null);
        var freeProject = await SeedProjectAsync();

        var result = await CreateManualAsync(freeProject);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.ExecutionNumber);
    }

    [Fact]
    public async Task Creation_for_a_missing_project_is_not_found_for_both_operations()
    {
        var manual = await CreateManualAsync(Guid.NewGuid());
        var simulated = await StartSimulatedAsync(Guid.NewGuid());

        Assert.Equal("projects.not_found", Assert.Single(manual.Errors).Code);
        Assert.Equal("projects.not_found", Assert.Single(simulated.Errors).Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void The_objective_must_not_be_blank(string objective)
    {
        var validator = new CreateManualRunCommandValidator();

        Assert.False(validator.Validate(new CreateManualRunCommand(Guid.NewGuid(), objective)).IsValid);
    }

    [Fact]
    public void The_objective_is_bounded_at_two_thousand_characters_and_the_project_is_required()
    {
        var validator = new CreateManualRunCommandValidator();

        Assert.True(validator.Validate(new CreateManualRunCommand(Guid.NewGuid(), new string('x', 2000))).IsValid);
        Assert.False(validator.Validate(new CreateManualRunCommand(Guid.NewGuid(), new string('x', 2001))).IsValid);
        Assert.False(validator.Validate(new CreateManualRunCommand(Guid.Empty, "Objective")).IsValid);
    }

    // The serialization proof: a competing intent commits strictly between this request's admission read and its single
    // save. Real interleaving is reproduced by construction, not by timing.
    [Theory]
    [InlineData("manual", "manual")]
    [InlineData("manual", "simulated")]
    [InlineData("simulated", "manual")]
    public async Task A_competing_intent_committed_before_the_save_rolls_back_the_loser_completely(string loser, string winner)
    {
        var projectId = await SeedProjectAsync();
        await using var inner = fixture.CreateContext();
        var faulting = new FaultInjectingDbContext(inner)
        {
            BeforeSaveChanges = async _ =>
            {
                var competing = winner == "manual"
                    ? (await CreateManualAsync(projectId, "Winner")).IsSuccess
                    : (await StartSimulatedAsync(projectId, "Winner")).IsSuccess;
                Assert.True(competing);
            },
        };

        var error = loser == "manual"
            ? Assert.Single((await new CreateManualRunCommandHandler(faulting, new FixedTimeProvider(Now))
                .HandleAsync(new CreateManualRunCommand(projectId, "Loser"), CancellationToken.None)).Errors)
            : Assert.Single((await new StartSimulatedRunCommandHandler(faulting, new FixedTimeProvider(Now))
                .HandleAsync(new StartSimulatedRunCommand(projectId, "Loser"), CancellationToken.None)).Errors);

        Assert.Equal(RunIntentRecorder.ConcurrentIntentCode, error.Code);
        await using var verify = fixture.CreateContext();
        var run = Assert.Single(verify.Runs.Where(candidate => candidate.ProjectId == projectId));
        Assert.Equal("Winner", run.Objective);
        Assert.Equal(1, run.ExecutionNumber);
        Assert.Single(verify.Events.Where(runEvent => runEvent.RunId == run.Id));
        Assert.Equal(2, verify.Projects.Single(project => project.Id == projectId).NextExecutionNumber);
    }

    [Fact]
    public async Task Parallel_manual_and_simulated_requests_create_exactly_one_run()
    {
        var projectId = await SeedProjectAsync();

        var attempts = Enumerable.Range(0, 8).Select(index => index % 2 == 0
            ? Task.Run(async () => (await CreateManualAsync(projectId, $"Parallel {index}")).IsSuccess)
            : Task.Run(async () => (await StartSimulatedAsync(projectId, $"Parallel {index}")).IsSuccess)).ToArray();
        var outcomes = await Task.WhenAll(attempts);

        Assert.Equal(1, outcomes.Count(success => success));
        var counts = await CountsAsync(projectId);
        Assert.Equal((1, 1, 2), counts);
    }

    [Fact]
    public async Task Consecutive_terminal_histories_keep_execution_numbers_strictly_monotonic()
    {
        var projectId = await SeedProjectAsync();
        var numbers = new List<int>();
        for (var index = 0; index < 3; index++)
        {
            var created = await CreateManualAsync(projectId, $"Round {index}");
            Assert.True(created.IsSuccess);
            numbers.Add(created.Value.ExecutionNumber);

            await using var dbContext = fixture.CreateContext();
            var run = await dbContext.Runs.SingleAsync(candidate => candidate.Id == created.Value.RunId);
            run.Claim(Now);
            run.Fail(Now);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        Assert.Equal([1, 2, 3], numbers);
    }

    [Fact]
    public async Task The_stored_mode_survives_a_fresh_read_and_an_undefined_number_is_not_a_recognized_mode()
    {
        var projectId = await SeedProjectAsync();
        var created = await CreateManualAsync(projectId);
        await RunExecutionModeTestSupport.SetStoredModeAsync(fixture, created.Value.RunId, RunExecutionModeTestSupport.UndefinedMode);

        await using var dbContext = fixture.CreateContext();
        var run = await dbContext.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == created.Value.RunId);

        Assert.False(RunExecutionModeAdmission.IsRecognized(run.ExecutionMode));
        Assert.False(RunExecutionModeAdmission.AdmitsAgent(run.ExecutionMode));
        Assert.False(RunExecutionModeAdmission.AdmitsSimulation(run.ExecutionMode));
        Assert.False(RunExecutionModeAdmission.AdmitsProcess(run.ExecutionMode));
    }
}
