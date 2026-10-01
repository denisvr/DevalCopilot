using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.ClaimProcessAttempt;
using DevalCopilot.Application.Features.Runs.Commands.ClaimSimulatedRun;
using DevalCopilot.Application.Features.Runs.Commands.CompleteSimulatedRun;
using DevalCopilot.Application.Features.Runs.Commands.CreateChallengeResolutionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateClaudeCriticalReviewAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodeReviewAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodexPlanningAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateImplementationAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateReviewCorrectionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.MarkProcessAttemptDispatched;
using DevalCopilot.Application.Features.Runs.Commands.RecordCollaborationMessage;
using DevalCopilot.Application.Features.Runs.Commands.RecordSimulatedAgentStep;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>The execution-mode admission table at every claim, record, and completion boundary that precedes
/// external work. Each refusal is reached before any workspace, evidence, manifest, or provider collaborator is
/// touched, so those collaborators are deliberately null here: reaching one would fail the test.</summary>
public sealed class RunExecutionModeAdmissionTests(SqliteDatabaseFixture fixture) : IClassFixture<SqliteDatabaseFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 11, 0, 0, TimeSpan.Zero);

    private const int Legacy = (int)RunExecutionMode.Legacy;
    private const int Simulated = (int)RunExecutionMode.Simulated;
    private const int ManualAgent = (int)RunExecutionMode.ManualAgent;
    private const int Undefined = RunExecutionModeTestSupport.UndefinedMode;

    public static TheoryData<string> AgentPaths() =>
        ["planning", "planning-repair", "critical-review", "critical-review-repair", "challenge-resolution",
         "challenge-resolution-repair", "implementation", "code-review", "code-review-repair", "review-correction"];

    internal static Task<Result<object>> InvokeAgentClaimAsync(string path, DevalCopilotDbContext dbContext, Guid runId)
    {
        var time = new FixedTimeProvider(Now);
        var other = Guid.NewGuid();
        return path switch
        {
            "planning" => Adapt(new CreateCodexPlanningAttemptCommandHandler(dbContext, null!, null!, time, null!)
                .HandleAsync(new CreateCodexPlanningAttemptCommand(runId), CancellationToken.None)),
            "planning-repair" => Adapt(new CreateCodexPlanningAttemptCommandHandler(dbContext, null!, null!, time, null!)
                .HandleAsync(new CreateCodexPlanningAttemptCommand(runId, other), CancellationToken.None)),
            "critical-review" => Adapt(new CreateClaudeCriticalReviewAttemptCommandHandler(dbContext, null!, null!, time, null!)
                .HandleAsync(new CreateClaudeCriticalReviewAttemptCommand(runId, other), CancellationToken.None)),
            "critical-review-repair" => Adapt(new CreateClaudeCriticalReviewAttemptCommandHandler(dbContext, null!, null!, time, null!)
                .HandleAsync(new CreateClaudeCriticalReviewAttemptCommand(runId, null, other), CancellationToken.None)),
            "challenge-resolution" => Adapt(new CreateChallengeResolutionAttemptCommandHandler(dbContext, null!, null!, time, null!)
                .HandleAsync(new CreateChallengeResolutionAttemptCommand(runId, other), CancellationToken.None)),
            "challenge-resolution-repair" => Adapt(new CreateChallengeResolutionAttemptCommandHandler(dbContext, null!, null!, time, null!)
                .HandleAsync(new CreateChallengeResolutionAttemptCommand(runId, null, other), CancellationToken.None)),
            "implementation" => Adapt(new CreateImplementationAttemptCommandHandler(dbContext, null!, null!, time)
                .HandleAsync(new CreateImplementationAttemptCommand(runId, other), CancellationToken.None)),
            "code-review" => Adapt(new CreateCodeReviewAttemptCommandHandler(dbContext, null!, null!, time, null!)
                .HandleAsync(new CreateCodeReviewAttemptCommand(runId, other), CancellationToken.None)),
            "code-review-repair" => Adapt(new CreateCodeReviewAttemptCommandHandler(dbContext, null!, null!, time, null!)
                .HandleAsync(new CreateCodeReviewAttemptCommand(runId, null, other), CancellationToken.None)),
            "review-correction" => Adapt(new CreateReviewCorrectionAttemptCommandHandler(dbContext, null!, null!, time)
                .HandleAsync(new CreateReviewCorrectionAttemptCommand(runId, other), CancellationToken.None)),
            _ => throw new ArgumentOutOfRangeException(nameof(path)),
        };
    }

    private static async Task<Result<object>> Adapt<T>(Task<Result<T>> pending)
    {
        var result = await pending;
        return result.IsFailure ? Result<object>.Failure(result.Errors[0]) : Result<object>.Success(result.Value!);
    }

    /// <summary>A project and one run of the given stored mode; <paramref name="running"/> leaves it claimed.</summary>
    private async Task<Guid> SeedRunAsync(int storedMode, bool running)
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Objective", Now);
        if (running)
        {
            run.Claim(Now);
        }

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await RunExecutionModeTestSupport.SetStoredModeAsync(dbContext, run.Id, storedMode);
        return run.Id;
    }

    [Theory]
    [MemberData(nameof(AgentPaths))]
    public async Task Every_agent_claim_path_refuses_a_simulated_or_undefined_mode_before_any_external_work(string path)
    {
        foreach (var storedMode in new[] { Simulated, Undefined })
        {
            var runId = await SeedRunAsync(storedMode, running: false);
            await using var dbContext = fixture.CreateContext();

            var result = await InvokeAgentClaimAsync(path, dbContext, runId);

            Assert.True(result.IsFailure);
            Assert.Equal(CurrentRunExecutionMode.NotAdmittedCode, Assert.Single(result.Errors).Code);
            await using var verify = fixture.CreateContext();
            Assert.Empty(verify.Attempts.Where(attempt => attempt.RunId == runId));
            Assert.Empty(verify.Artifacts.Where(artifact => artifact.RunId == runId));
            Assert.Equal(RunLifecycle.Created, verify.Runs.Single(run => run.Id == runId).Lifecycle);
        }
    }

    [Theory]
    [MemberData(nameof(AgentPaths))]
    public async Task Every_agent_claim_path_passes_the_mode_gate_for_manual_and_legacy_runs(string path)
    {
        foreach (var storedMode in new[] { ManualAgent, Legacy })
        {
            var runId = await SeedRunAsync(storedMode, running: false);
            await using var dbContext = fixture.CreateContext();

            // No workspace exists, so the claim fails later and for another reason: the mode was admitted.
            var result = await InvokeAgentClaimAsync(path, dbContext, runId);

            Assert.True(result.IsFailure);
            Assert.NotEqual(CurrentRunExecutionMode.NotAdmittedCode, Assert.Single(result.Errors).Code);
        }
    }

    [Theory]
    [MemberData(nameof(AgentPaths))]
    public async Task A_tracked_stale_run_never_confers_authority_to_an_agent_claim(string path)
    {
        var runId = await SeedRunAsync(Legacy, running: false);
        await using var dbContext = fixture.CreateContext();
        var tracked = await dbContext.Runs.SingleAsync(run => run.Id == runId);
        Assert.Equal(RunExecutionMode.Legacy, tracked.ExecutionMode);
        await RunExecutionModeTestSupport.SetStoredModeAsync(dbContext, runId, Simulated);

        var result = await InvokeAgentClaimAsync(path, dbContext, runId);

        Assert.Equal(RunExecutionMode.Legacy, tracked.ExecutionMode);
        Assert.Equal(CurrentRunExecutionMode.NotAdmittedCode, Assert.Single(result.Errors).Code);
        await using var verify = fixture.CreateContext();
        Assert.Empty(verify.Attempts.Where(attempt => attempt.RunId == runId));
    }

    [Theory]
    [InlineData(Simulated, true)]
    [InlineData(Legacy, true)]
    [InlineData(ManualAgent, false)]
    [InlineData(Undefined, false)]
    public async Task The_simulation_claim_admits_only_simulated_and_legacy_runs(int storedMode, bool admitted)
    {
        var runId = await SeedRunAsync(storedMode, running: false);
        await using var dbContext = fixture.CreateContext();

        var result = await new ClaimSimulatedRunCommandHandler(dbContext, new FixedTimeProvider(Now))
            .HandleAsync(new ClaimSimulatedRunCommand(runId), CancellationToken.None);

        await using var verify = fixture.CreateContext();
        if (admitted)
        {
            Assert.True(result.IsSuccess);
            Assert.Equal(RunLifecycle.Running, verify.Runs.Single(run => run.Id == runId).Lifecycle);
            Assert.Single(verify.Attempts.Where(attempt => attempt.RunId == runId && attempt.Kind == AttemptKind.Simulated));
        }
        else
        {
            Assert.Equal(CurrentRunExecutionMode.NotAdmittedCode, Assert.Single(result.Errors).Code);
            Assert.Equal(RunLifecycle.Created, verify.Runs.Single(run => run.Id == runId).Lifecycle);
            Assert.Empty(verify.Attempts.Where(attempt => attempt.RunId == runId));
        }
    }

    [Fact]
    public async Task A_competing_mode_change_before_the_simulation_claim_commits_persists_no_attempt()
    {
        var runId = await SeedRunAsync(Simulated, running: false);
        await using var inner = fixture.CreateContext();
        var faulting = new FaultInjectingDbContext(inner)
        {
            BeforeSaveChanges = _ => RunExecutionModeTestSupport.SetStoredModeAsync(fixture, runId, ManualAgent),
        };

        var result = await new ClaimSimulatedRunCommandHandler(faulting, new FixedTimeProvider(Now))
            .HandleAsync(new ClaimSimulatedRunCommand(runId), CancellationToken.None);

        Assert.Equal(CurrentRunExecutionMode.ChangedDuringClaimCode, Assert.Single(result.Errors).Code);
        await using var verify = fixture.CreateContext();
        Assert.Empty(verify.Attempts.Where(attempt => attempt.RunId == runId));
        Assert.Equal(RunLifecycle.Created, verify.Runs.Single(run => run.Id == runId).Lifecycle);
    }

    [Theory]
    [InlineData(Legacy, true)]
    [InlineData(Simulated, false)]
    [InlineData(ManualAgent, false)]
    [InlineData(Undefined, false)]
    public async Task The_standalone_process_claim_admits_only_legacy_runs(int storedMode, bool admitted)
    {
        var runId = await SeedRunAsync(storedMode, running: false);
        await using var dbContext = fixture.CreateContext();
        var intent = new ProcessExecutionIntent(@"C:\tools\build.exe", ["--verify"], @"C:\repos\x", @"C:\repos", TimeSpan.FromMinutes(1), 1024, 2048);

        var result = await new ClaimProcessAttemptCommandHandler(dbContext, new FixedTimeProvider(Now))
            .HandleAsync(new ClaimProcessAttemptCommand(runId, intent), CancellationToken.None);

        await using var verify = fixture.CreateContext();
        Assert.Equal(admitted, result.IsSuccess);
        Assert.Equal(admitted ? 1 : 0, verify.Attempts.Count(attempt => attempt.RunId == runId && attempt.Kind == AttemptKind.Process));
        if (!admitted)
        {
            Assert.Equal(CurrentRunExecutionMode.NotAdmittedCode, Assert.Single(result.Errors).Code);
        }
    }

    [Theory]
    [InlineData(Legacy, true)]
    [InlineData(Simulated, false)]
    [InlineData(ManualAgent, false)]
    [InlineData(Undefined, false)]
    public async Task Process_dispatch_is_admitted_only_for_a_legacy_run(int storedMode, bool admitted)
    {
        var runId = await SeedRunAsync(Legacy, running: true);
        Guid attemptId;
        await using (var seed = fixture.CreateContext())
        {
            var intent = new ProcessExecutionIntent(@"C:\tools\build.exe", ["--verify"], @"C:\repos\x", @"C:\repos", TimeSpan.FromMinutes(1), 1024, 2048);
            var attempt = Attempt.ClaimProcess(Guid.NewGuid(), runId, 1, intent, Now);
            seed.Attempts.Add(attempt);
            await seed.SaveChangesAsync(CancellationToken.None);
            attemptId = attempt.Id;
        }

        await RunExecutionModeTestSupport.SetStoredModeAsync(fixture, runId, storedMode);
        await using var dbContext = fixture.CreateContext();
        var result = await new MarkProcessAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now))
            .HandleAsync(new MarkProcessAttemptDispatchedCommand(runId, attemptId), CancellationToken.None);
        if (result.IsSuccess)
        {
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        await using var verify = fixture.CreateContext();
        Assert.Equal(admitted, result.IsSuccess);
        Assert.Equal(admitted, verify.Attempts.Single(attempt => attempt.Id == attemptId).ProcessDispatchedAtUtc.HasValue);
    }

    private async Task<(Guid RunId, Guid AttemptId)> SeedRunningSimulatedAttemptAsync(int storedMode)
    {
        var runId = await SeedRunAsync(Legacy, running: true);
        await using var seed = fixture.CreateContext();
        var attempt = Attempt.Claim(Guid.NewGuid(), runId, 1, Now);
        seed.Attempts.Add(attempt);
        await seed.SaveChangesAsync(CancellationToken.None);
        await RunExecutionModeTestSupport.SetStoredModeAsync(seed, runId, storedMode);
        return (runId, attempt.Id);
    }

    private static RecordSimulatedAgentStepCommand StepCommand(Guid runId, Guid attemptId) => new(
        runId,
        attemptId,
        RunStage.Plan,
        ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex),
        ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
        RunEventType.CodexProposal,
        CollaborationMessageType.Proposal,
        null,
        "Proposal",
        "{\"scope\":\"Test scope\",\"implementationSteps\":\"Test steps\",\"risks\":\"Test risk\",\"verificationPlan\":\"Test verification\",\"escalationPoints\":\"None expected\"}");

    [Theory]
    [InlineData(Simulated, true)]
    [InlineData(Legacy, true)]
    [InlineData(ManualAgent, false)]
    [InlineData(Undefined, false)]
    public async Task Recording_a_simulated_step_requires_a_compatible_mode(int storedMode, bool admitted)
    {
        var (runId, attemptId) = await SeedRunningSimulatedAttemptAsync(storedMode);
        await using var dbContext = fixture.CreateContext();

        var result = await new RecordSimulatedAgentStepCommandHandler(dbContext, new FixedTimeProvider(Now))
            .HandleAsync(StepCommand(runId, attemptId), CancellationToken.None);

        await using var verify = fixture.CreateContext();
        Assert.Equal(admitted, result.IsSuccess);
        Assert.Equal(admitted ? 1 : 0, verify.CollaborationMessages.Count(message => message.RunId == runId));
        if (!admitted)
        {
            Assert.Equal(CurrentRunExecutionMode.NotAdmittedCode, Assert.Single(result.Errors).Code);
            Assert.Equal(RunStage.Intake, verify.Runs.Single(run => run.Id == runId).Stage);
        }
    }

    [Theory]
    [InlineData(Simulated, true)]
    [InlineData(Legacy, true)]
    [InlineData(ManualAgent, false)]
    [InlineData(Undefined, false)]
    public async Task Completing_a_simulated_run_requires_a_compatible_mode(int storedMode, bool admitted)
    {
        var (runId, attemptId) = await SeedRunningSimulatedAttemptAsync(storedMode);
        await using var dbContext = fixture.CreateContext();

        var result = await new CompleteSimulatedRunCommandHandler(dbContext, new FixedTimeProvider(Now))
            .HandleAsync(new CompleteSimulatedRunCommand(runId, attemptId), CancellationToken.None);

        await using var verify = fixture.CreateContext();
        Assert.Equal(admitted, result.IsSuccess);
        Assert.Equal(admitted ? RunLifecycle.Completed : RunLifecycle.Running, verify.Runs.Single(run => run.Id == runId).Lifecycle);
    }

    [Theory]
    [InlineData(Simulated)]
    [InlineData(ManualAgent)]
    [InlineData(Legacy)]
    public async Task Direct_simulated_completion_can_never_complete_an_agent_attempt_or_its_run(int storedMode)
    {
        var runId = await SeedRunAsync(Legacy, running: true);
        Guid attemptId;
        await using (var seed = fixture.CreateContext())
        {
            var attempt = Attempt.ClaimAgent(
                Guid.NewGuid(), runId, 1, Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 262144, 524288, Now, 1);
            seed.Attempts.Add(attempt);
            await seed.SaveChangesAsync(CancellationToken.None);
            attemptId = attempt.Id;
            await RunExecutionModeTestSupport.SetStoredModeAsync(seed, runId, storedMode);
        }

        await using var dbContext = fixture.CreateContext();
        var result = await new CompleteSimulatedRunCommandHandler(dbContext, new FixedTimeProvider(Now))
            .HandleAsync(new CompleteSimulatedRunCommand(runId, attemptId), CancellationToken.None);

        Assert.Equal("attempts.simulated_completion_requires_simulated_attempt", Assert.Single(result.Errors).Code);
        await using var verify = fixture.CreateContext();
        Assert.Equal(RunLifecycle.Running, verify.Runs.Single(run => run.Id == runId).Lifecycle);
        Assert.Equal(AttemptStatus.Running, verify.Attempts.Single(attempt => attempt.Id == attemptId).Status);
        Assert.Empty(verify.Events.Where(runEvent => runEvent.RunId == runId && runEvent.EventType == RunEventType.RunCompleted));
    }

    [Fact]
    public async Task Simulated_completion_for_a_mismatched_run_and_attempt_identity_is_refused()
    {
        var (runId, _) = await SeedRunningSimulatedAttemptAsync(Simulated);
        var (_, otherAttemptId) = await SeedRunningSimulatedAttemptAsync(Simulated);
        await using var dbContext = fixture.CreateContext();

        var result = await new CompleteSimulatedRunCommandHandler(dbContext, new FixedTimeProvider(Now))
            .HandleAsync(new CompleteSimulatedRunCommand(runId, otherAttemptId), CancellationToken.None);

        Assert.Equal("attempts.not_found", Assert.Single(result.Errors).Code);
        await using var verify = fixture.CreateContext();
        Assert.Equal(RunLifecycle.Running, verify.Runs.Single(run => run.Id == runId).Lifecycle);
    }

    [Fact]
    public async Task A_competing_mode_change_before_simulated_completion_commits_leaves_the_run_running()
    {
        var (runId, attemptId) = await SeedRunningSimulatedAttemptAsync(Simulated);
        await using var inner = fixture.CreateContext();
        var faulting = new FaultInjectingDbContext(inner)
        {
            BeforeSaveChanges = _ => RunExecutionModeTestSupport.SetStoredModeAsync(fixture, runId, ManualAgent),
        };

        var result = await new CompleteSimulatedRunCommandHandler(faulting, new FixedTimeProvider(Now))
            .HandleAsync(new CompleteSimulatedRunCommand(runId, attemptId), CancellationToken.None);

        Assert.Equal(CurrentRunExecutionMode.ChangedDuringClaimCode, Assert.Single(result.Errors).Code);
        await using var verify = fixture.CreateContext();
        Assert.Equal(RunLifecycle.Running, verify.Runs.Single(run => run.Id == runId).Lifecycle);
        Assert.Equal(AttemptStatus.Running, verify.Attempts.Single(attempt => attempt.Id == attemptId).Status);
    }

    private static RecordCollaborationMessageCommand SimulatedMessage(Guid runId, Guid? attemptId) => new(
        Guid.NewGuid(),
        runId,
        attemptId,
        CollaborationMessage.ProtocolVersionOne,
        ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex),
        ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
        CollaborationMessageType.Proposal,
        null,
        "Proposal",
        "{\"scope\":\"Test scope\",\"implementationSteps\":\"Test steps\",\"risks\":\"Test risk\",\"verificationPlan\":\"Test verification\",\"escalationPoints\":\"None expected\"}",
        CollaborationMessageProvenance.Simulated);

    [Theory]
    [InlineData(Simulated, true)]
    [InlineData(Legacy, true)]
    [InlineData(ManualAgent, false)]
    [InlineData(Undefined, false)]
    public async Task The_generic_message_boundary_admits_simulated_content_only_for_a_compatible_mode(int storedMode, bool admitted)
    {
        var (runId, attemptId) = await SeedRunningSimulatedAttemptAsync(storedMode);
        await using var dbContext = fixture.CreateContext();

        var result = await new RecordCollaborationMessageCommandHandler(dbContext, new FixedTimeProvider(Now))
            .HandleAsync(SimulatedMessage(runId, attemptId), CancellationToken.None);

        await using var verify = fixture.CreateContext();
        Assert.Equal(admitted, result.IsSuccess);
        Assert.Equal(admitted ? 1 : 0, verify.CollaborationMessages.Count(message => message.RunId == runId));
    }

    [Fact]
    public async Task Simulated_provenance_without_an_attempt_is_also_refused_for_a_manual_run()
    {
        var runId = await SeedRunAsync(ManualAgent, running: false);
        await using var dbContext = fixture.CreateContext();

        var result = await new RecordCollaborationMessageCommandHandler(dbContext, new FixedTimeProvider(Now))
            .HandleAsync(SimulatedMessage(runId, null) with { Actor = ParticipantIdentity.ForOrchestrator() }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(fixture.CreateContext().CollaborationMessages.Where(message => message.RunId == runId));
    }

    [Theory]
    [InlineData(Simulated)]
    [InlineData(ManualAgent)]
    [InlineData(Legacy)]
    public async Task A_human_submitted_message_is_never_subject_to_the_mode_gate(int storedMode)
    {
        var runId = await SeedRunAsync(storedMode, running: false);
        await using var dbContext = fixture.CreateContext();

        var result = await new RecordCollaborationMessageCommandHandler(dbContext, new FixedTimeProvider(Now)).HandleAsync(
            new RecordCollaborationMessageCommand(
                Guid.NewGuid(),
                runId,
                null,
                CollaborationMessage.ProtocolVersionOne,
                ParticipantIdentity.ForHuman(),
                ParticipantIdentity.ForOrchestrator(),
                CollaborationMessageType.Question,
                null,
                "Question",
                "{\"question\":\"Is the scope right?\",\"why\":\"Review load.\"}",
                CollaborationMessageProvenance.HumanSubmitted),
            CancellationToken.None);

        // Rejected only by the message protocol (a question needs a reply reference), never by the mode gate.
        Assert.True(result.IsFailure);
        Assert.Equal("collaboration_messages.reply_required", Assert.Single(result.Errors).Code);
    }
}
