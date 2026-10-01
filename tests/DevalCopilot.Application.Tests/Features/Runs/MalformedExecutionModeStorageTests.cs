using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.ClaimProcessAttempt;
using DevalCopilot.Application.Features.Runs.Commands.ClaimSimulatedRun;
using DevalCopilot.Application.Features.Runs.Commands.CompleteSimulatedRun;
using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Application.Features.Runs.Commands.MarkProcessAttemptDispatched;
using DevalCopilot.Application.Features.Runs.Commands.RecordSimulatedAgentStep;
using DevalCopilot.Application.Features.Runs.Queries.GetEligibleAgentAttempts;
using DevalCopilot.Application.Features.Runs.Queries.GetEligibleChallengeResolutionAttempts;
using DevalCopilot.Application.Features.Runs.Queries.GetEligibleClaudeCriticalReviewAttempts;
using DevalCopilot.Application.Features.Runs.Queries.GetEligibleCodeReviewAttempts;
using DevalCopilot.Application.Features.Runs.Queries.GetEligibleImplementationAttempts;
using DevalCopilot.Application.Features.Runs.Queries.GetEligibleProcessAttempts;
using DevalCopilot.Application.Features.Runs.Queries.GetEligibleReviewCorrectionAttempts;
using DevalCopilot.Application.Features.Runs.Queries.GetEligibleSimulatedRuns;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// A stored execution mode is authoritative only as the exact integers 0, 1, or 2. A REAL that truncates to a valid
/// mode, an integer that overflows 32 bits, text, and BLOBs are malformed: they are never coerced into a recognized
/// mode, never throw during materialization, and are never rewritten. Each test owns a fresh file-backed database
/// because the feeds scan every attempt.
/// </summary>
public sealed class MalformedExecutionModeStorageTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
    private const int ManualAgent = (int)RunExecutionMode.ManualAgent;

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    public static TheoryData<string> Malformed() => [.. RunExecutionModeTestSupport.MalformedLiterals];

    public static TheoryData<string, string> AgentPathsByMalformed()
    {
        var data = new TheoryData<string, string>();
        foreach (var path in new[]
        {
            "planning", "planning-repair", "critical-review", "critical-review-repair", "challenge-resolution",
            "challenge-resolution-repair", "implementation", "code-review", "code-review-repair", "review-correction",
        })
        {
            foreach (var literal in RunExecutionModeTestSupport.MalformedLiterals)
            {
                data.Add(path, literal);
            }
        }

        return data;
    }

    public static TheoryData<string, string> FeedsByMalformed()
    {
        var data = new TheoryData<string, string>();
        foreach (var feed in new[] { "planning", "critical-review", "challenge-resolution", "implementation", "code-review", "review-correction" })
        {
            foreach (var literal in RunExecutionModeTestSupport.MalformedLiterals)
            {
                data.Add(feed, literal);
            }
        }

        return data;
    }

    private async Task<Guid> SeedRunAsync(string literal, bool running)
    {
        await using var dbContext = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Objective", Now);
        if (running)
        {
            run.Claim(Now);
        }

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await RunExecutionModeTestSupport.SetStoredRawAsync(dbContext, run.Id, literal);
        return run.Id;
    }

    [Theory]
    [MemberData(nameof(AgentPathsByMalformed))]
    public async Task Every_agent_claim_path_refuses_a_malformed_stored_mode_before_any_external_work(string path, string literal)
    {
        var runId = await SeedRunAsync(literal, running: false);
        await using var dbContext = _fixture.CreateContext();

        var result = await RunExecutionModeAdmissionTests.InvokeAgentClaimAsync(path, dbContext, runId);

        Assert.True(result.IsFailure);
        Assert.Equal(CurrentRunExecutionMode.NotAdmittedCode, Assert.Single(result.Errors).Code);
        await using var verify = _fixture.CreateContext();
        Assert.Empty(verify.Attempts.Where(attempt => attempt.RunId == runId));
        Assert.Empty(verify.Artifacts.Where(artifact => artifact.RunId == runId));
    }

    [Theory]
    [MemberData(nameof(FeedsByMalformed))]
    public async Task Each_agent_feed_omits_a_malformed_run_and_keeps_its_healthy_sibling(string feed, string literal)
    {
        var healthy = await AgentAttemptModeSeed.SeedClaimedAsync(_fixture, feed, ManualAgent);
        var malformed = await AgentAttemptModeSeed.SeedClaimedRawAsync(_fixture, feed, literal);
        await using var dbContext = _fixture.CreateContext();

        IReadOnlyList<Guid> eligible = feed switch
        {
            "planning" => (await new GetEligibleAgentAttemptsQueryHandler(dbContext).HandleAsync(new GetEligibleAgentAttemptsQuery(), default)).Select(a => a.AttemptId).ToArray(),
            "critical-review" => (await new GetEligibleClaudeCriticalReviewAttemptsQueryHandler(dbContext).HandleAsync(new GetEligibleClaudeCriticalReviewAttemptsQuery(), default)).Select(a => a.AttemptId).ToArray(),
            "challenge-resolution" => (await new GetEligibleChallengeResolutionAttemptsQueryHandler(dbContext).HandleAsync(new GetEligibleChallengeResolutionAttemptsQuery(), default)).Select(a => a.AttemptId).ToArray(),
            "implementation" => (await new GetEligibleImplementationAttemptsQueryHandler(dbContext).HandleAsync(new GetEligibleImplementationAttemptsQuery(), default)).Select(a => a.AttemptId).ToArray(),
            "code-review" => (await new GetEligibleCodeReviewAttemptsQueryHandler(dbContext).HandleAsync(new GetEligibleCodeReviewAttemptsQuery(), default)).Select(a => a.AttemptId).ToArray(),
            _ => (await new GetEligibleReviewCorrectionAttemptsQueryHandler(dbContext).HandleAsync(new GetEligibleReviewCorrectionAttemptsQuery(), default)).Select(a => a.AttemptId).ToArray(),
        };

        Assert.Contains(healthy.AttemptId, eligible);
        Assert.DoesNotContain(malformed.AttemptId, eligible);
    }

    [Theory]
    [MemberData(nameof(Malformed))]
    public async Task Final_dispatch_refuses_a_malformed_stored_mode(string literal)
    {
        var (runId, attemptId) = await AgentAttemptModeSeed.SeedClaimedRawAsync(_fixture, "planning", literal);
        await using var dbContext = _fixture.CreateContext();

        var result = await new MarkAgentAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now))
            .HandleAsync(new MarkAgentAttemptDispatchedCommand(runId, attemptId), CancellationToken.None);

        Assert.Equal(CurrentRunExecutionMode.NotAdmittedCode, Assert.Single(result.Errors).Code);
        await using var verify = _fixture.CreateContext();
        Assert.Null((await verify.Attempts.AsNoTracking().SingleAsync(a => a.Id == attemptId)).AgentDispatchedAtUtc);
    }

    [Theory]
    [MemberData(nameof(Malformed))]
    public async Task The_in_transaction_confirmation_refuses_a_malformed_stored_mode(string literal)
    {
        var runId = await SeedRunAsync(literal, running: false);
        await using var dbContext = _fixture.CreateContext();
        var run = await dbContext.Runs.SingleAsync(candidate => candidate.Id == runId);

        Assert.False(await CurrentRunExecutionMode.ConfirmAgentAdmittedAsync(dbContext, run, CancellationToken.None));
    }

    [Theory]
    [MemberData(nameof(Malformed))]
    public async Task Simulation_claim_feed_step_and_completion_refuse_a_malformed_stored_mode(string literal)
    {
        var runId = await SeedRunAsync(literal, running: false);

        await using (var claimContext = _fixture.CreateContext())
        {
            var claim = await new ClaimSimulatedRunCommandHandler(claimContext, new FixedTimeProvider(Now))
                .HandleAsync(new ClaimSimulatedRunCommand(runId), CancellationToken.None);
            Assert.Equal(CurrentRunExecutionMode.NotAdmittedCode, Assert.Single(claim.Errors).Code);
        }

        await using (var feedContext = _fixture.CreateContext())
        {
            var feed = await new GetEligibleSimulatedRunsQueryHandler(feedContext)
                .HandleAsync(new GetEligibleSimulatedRunsQuery(), CancellationToken.None);
            Assert.DoesNotContain(runId, feed);
        }

        var runningRunId = await SeedRunAsync("1", running: true);
        Guid attemptId;
        await using (var seed = _fixture.CreateContext())
        {
            var attempt = Attempt.Claim(Guid.NewGuid(), runningRunId, 1, Now);
            seed.Attempts.Add(attempt);
            await seed.SaveChangesAsync(CancellationToken.None);
            attemptId = attempt.Id;
            await RunExecutionModeTestSupport.SetStoredRawAsync(seed, runningRunId, literal);
        }

        await using var context = _fixture.CreateContext();
        var step = await new RecordSimulatedAgentStepCommandHandler(context, new FixedTimeProvider(Now)).HandleAsync(
            new RecordSimulatedAgentStepCommand(
                runningRunId, attemptId, RunStage.Plan,
                ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex),
                ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
                RunEventType.CodexProposal, CollaborationMessageType.Proposal, null, "Proposal",
                "{\"scope\":\"s\",\"implementationSteps\":\"s\",\"risks\":\"r\",\"verificationPlan\":\"v\",\"escalationPoints\":\"n\"}"),
            CancellationToken.None);
        Assert.Equal(CurrentRunExecutionMode.NotAdmittedCode, Assert.Single(step.Errors).Code);

        var completion = await new CompleteSimulatedRunCommandHandler(context, new FixedTimeProvider(Now))
            .HandleAsync(new CompleteSimulatedRunCommand(runningRunId, attemptId), CancellationToken.None);
        Assert.Equal(CurrentRunExecutionMode.NotAdmittedCode, Assert.Single(completion.Errors).Code);
        await using var verify = _fixture.CreateContext();
        Assert.Equal(RunLifecycle.Running, verify.Runs.AsNoTracking().Single(run => run.Id == runningRunId).Lifecycle);
    }

    [Theory]
    [MemberData(nameof(Malformed))]
    public async Task The_process_claim_feed_and_dispatch_refuse_a_malformed_stored_mode(string literal)
    {
        var intent = new ProcessExecutionIntent(@"C:\tools\build.exe", ["--verify"], @"C:\repos\x", @"C:\repos", TimeSpan.FromMinutes(1), 1024, 2048);
        var createdRunId = await SeedRunAsync(literal, running: false);
        await using (var claimContext = _fixture.CreateContext())
        {
            var claim = await new ClaimProcessAttemptCommandHandler(claimContext, new FixedTimeProvider(Now))
                .HandleAsync(new ClaimProcessAttemptCommand(createdRunId, intent), CancellationToken.None);
            Assert.Equal(CurrentRunExecutionMode.NotAdmittedCode, Assert.Single(claim.Errors).Code);
        }

        var runId = await SeedRunAsync("0", running: true);
        Guid attemptId;
        await using (var seed = _fixture.CreateContext())
        {
            var attempt = Attempt.ClaimProcess(Guid.NewGuid(), runId, 1, intent, Now);
            seed.Attempts.Add(attempt);
            await seed.SaveChangesAsync(CancellationToken.None);
            attemptId = attempt.Id;
            await RunExecutionModeTestSupport.SetStoredRawAsync(seed, runId, literal);
        }

        await using var context = _fixture.CreateContext();
        var feed = await new GetEligibleProcessAttemptsQueryHandler(context)
            .HandleAsync(new GetEligibleProcessAttemptsQuery(), CancellationToken.None);
        Assert.DoesNotContain(feed, candidate => candidate.AttemptId == attemptId);
        var dispatch = await new MarkProcessAttemptDispatchedCommandHandler(context, new FixedTimeProvider(Now))
            .HandleAsync(new MarkProcessAttemptDispatchedCommand(runId, attemptId), CancellationToken.None);
        Assert.Equal(CurrentRunExecutionMode.NotAdmittedCode, Assert.Single(dispatch.Errors).Code);
    }

    [Theory]
    [MemberData(nameof(Malformed))]
    public async Task An_unrelated_save_preserves_the_exact_malformed_stored_value(string literal)
    {
        var runId = await SeedRunAsync(literal, running: false);
        var before = await RunExecutionModeTestSupport.ReadStoredRawAsync(_fixture, runId);

        await using (var dbContext = _fixture.CreateContext())
        {
            var run = await dbContext.Runs.SingleAsync(candidate => candidate.Id == runId);
            Assert.False(RunExecutionModeAdmission.IsRecognized(run.ExecutionMode));
            run.SetRequestedCodexAssignment("gpt-5", "low");
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        Assert.Equal(before, await RunExecutionModeTestSupport.ReadStoredRawAsync(_fixture, runId));
    }

    [Fact]
    public async Task Exact_valid_integers_still_authorize_and_are_stored_as_integers()
    {
        foreach (var (mode, simulation, agent, process) in new[] { (0, true, true, true), (1, true, false, false), (2, false, true, false) })
        {
            var runId = await SeedRunAsync(mode.ToString(), running: false);
            await using var dbContext = _fixture.CreateContext();
            var run = await dbContext.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == runId);
            Assert.Equal((RunExecutionMode)mode, run.ExecutionMode);
            Assert.Equal(simulation, RunExecutionModeAdmission.AdmitsSimulation(run.ExecutionMode));
            Assert.Equal(agent, RunExecutionModeAdmission.AdmitsAgent(run.ExecutionMode));
            Assert.Equal(process, RunExecutionModeAdmission.AdmitsProcess(run.ExecutionMode));
            Assert.Equal(("integer", mode.ToString()), await RunExecutionModeTestSupport.ReadStoredRawAsync(_fixture, runId));
        }
    }
}
