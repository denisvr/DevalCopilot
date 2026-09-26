using System.Data.Common;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Queries.GetRunCockpit;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// End-to-end coverage of the per-claim-path advisory time-fit projection as
/// <c>GetRunCockpitQueryHandler</c> actually produces it: real SQLite evidence, not a hand-built
/// <see cref="RunCockpitAgentInvocationTimeBudgetSummary"/>. Confirms the four honest states
/// (fits at the exact boundary, legacy-unknown, evidence-invalid) reach the query result, and that
/// adding this projection issues no additional database round trip beyond the handler's existing
/// single bounded pass.
/// </summary>
public sealed class GetRunCockpitQueryHandlerAgentClaimPathTimeFitTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private sealed class CommandCountingInterceptor : DbCommandInterceptor
    {
        private int _commandCount;

        public int CommandCount => Volatile.Read(ref _commandCount);

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Interlocked.Increment(ref _commandCount);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _commandCount);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private static (Project Project, Run Run, GitWorkspace Workspace, GitCheckpoint Checkpoint) BuildScaffold(
        TimeSpan? maximumAgentInvocationTime)
    {
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(
            Guid.NewGuid(), project.Id, 1, "Ship the slice", Now, maximumAgentInvocationTime: maximumAgentInvocationTime);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        workspace.MarkReady();
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), Fingerprint, []);
        return (project, run, workspace, checkpoint);
    }

    private static Attempt ClaimAgentAttempt(Guid runId, Guid workspaceId, Guid checkpointId, int attemptNumber, TimeSpan timeout)
    {
        var attempt = Attempt.ClaimAgent(
            Guid.NewGuid(), runId, attemptNumber, workspaceId, checkpointId, Fingerprint, Guid.NewGuid(),
            timeout, 262144, 524288, Now, attemptNumber);
        // Completed (never left Running) so more than one can coexist on the same run: the schema
        // enforces at most one Running attempt per run at a time (ix_attempts_run_id_one_running).
        attempt.MarkAgentDispatched(Now);
        attempt.CompleteAgent(AgentOutcome.Proposed, Fingerprint, Now, processEvidence: TestProcessEvidence.CleanExit);
        return attempt;
    }

    [Fact]
    public async Task A_run_budgeted_with_exactly_ten_minutes_remaining_fits_every_ten_minute_path_and_rejects_every_twenty_minute_path()
    {
        await using var dbContext = _fixture.CreateContext();
        var (project, run, workspace, checkpoint) = BuildScaffold(TimeSpan.FromMinutes(10));

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.GitWorkspaces.Add(workspace);
        dbContext.GitCheckpoints.Add(checkpoint);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetRunCockpitQueryHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(new GetRunCockpitQuery(run.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var byPath = result.Value.AgentClaimPathTimeFits.ToDictionary(entry => entry.ClaimPath);
        Assert.Equal(AgentClaimPathTimeFit.Fits, byPath[AgentClaimPath.CodexPlanning].Fit);
        Assert.Equal(AgentClaimPathTimeFit.Fits, byPath[AgentClaimPath.ClaudeCriticalReview].Fit);
        Assert.Equal(AgentClaimPathTimeFit.Fits, byPath[AgentClaimPath.ChallengeResolution].Fit);
        Assert.Equal(AgentClaimPathTimeFit.Fits, byPath[AgentClaimPath.CodeReview].Fit);
        Assert.Equal(AgentClaimPathTimeFit.DoesNotFit, byPath[AgentClaimPath.Implementation].Fit);
        Assert.Equal(AgentClaimPathTimeFit.DoesNotFit, byPath[AgentClaimPath.ReviewCorrection].Fit);
    }

    [Fact]
    public async Task A_legacy_run_with_no_time_policy_reports_LegacyUnknown_for_every_claim_path()
    {
        await using var dbContext = _fixture.CreateContext();
        var (project, run, workspace, checkpoint) = BuildScaffold(maximumAgentInvocationTime: TimeSpan.FromMinutes(120));
        // Run.RecordIntent always assigns a real default (never null); overwritten here purely to
        // simulate a historical Run that predates ADR-0013's policy.
        RunMaximumAgentInvocationTimeSubstitution.SetNull(run);

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.GitWorkspaces.Add(workspace);
        dbContext.GitCheckpoints.Add(checkpoint);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetRunCockpitQueryHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(new GetRunCockpitQuery(run.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.All(result.Value.AgentClaimPathTimeFits, entry => Assert.Equal(AgentClaimPathTimeFit.LegacyUnknown, entry.Fit));
    }

    [Fact]
    public async Task A_run_with_a_non_positive_persisted_agent_timeout_reports_EvidenceInvalid_for_every_claim_path()
    {
        await using var dbContext = _fixture.CreateContext();
        var (project, run, workspace, checkpoint) = BuildScaffold(TimeSpan.FromMinutes(120));

        var malformedAttempt = ClaimAgentAttempt(run.Id, workspace.Id, checkpoint.Id, 1, TimeSpan.FromMinutes(10));
        AttemptTimeoutSubstitution.SetTimeout(malformedAttempt, TimeSpan.Zero);

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.GitWorkspaces.Add(workspace);
        dbContext.GitCheckpoints.Add(checkpoint);
        dbContext.Attempts.Add(malformedAttempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetRunCockpitQueryHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(new GetRunCockpitQuery(run.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.All(result.Value.AgentClaimPathTimeFits, entry => Assert.Equal(AgentClaimPathTimeFit.EvidenceInvalid, entry.Fit));
    }

    /// <summary>
    /// The claim-path fit projection reuses the run-wide reserved-time computation already made
    /// for <c>AgentInvocationTimeBudget</c> and evaluates all six claim paths purely in memory —
    /// it must never add a database round trip, regardless of how many Agent attempts the run has
    /// already claimed.
    /// </summary>
    [Fact]
    public async Task Database_command_count_is_independent_of_the_number_of_previously_claimed_agent_attempts()
    {
        var zeroAttemptsCount = await MeasureCommandCountAsync(claimedAttemptCount: 0);
        var fiveAttemptsCount = await MeasureCommandCountAsync(claimedAttemptCount: 5);

        Assert.Equal(zeroAttemptsCount, fiveAttemptsCount);
    }

    private async Task<int> MeasureCommandCountAsync(int claimedAttemptCount)
    {
        Guid runId;
        await using (var seedContext = _fixture.CreateContext())
        {
            var (project, run, workspace, checkpoint) = BuildScaffold(TimeSpan.FromMinutes(120));
            seedContext.Projects.Add(project);
            seedContext.Runs.Add(run);
            seedContext.GitWorkspaces.Add(workspace);
            seedContext.GitCheckpoints.Add(checkpoint);
            for (var index = 0; index < claimedAttemptCount; index++)
            {
                seedContext.Attempts.Add(ClaimAgentAttempt(run.Id, workspace.Id, checkpoint.Id, index + 1, TimeSpan.FromMinutes(10)));
            }

            await seedContext.SaveChangesAsync(CancellationToken.None);
            runId = run.Id;
        }

        var interceptor = new CommandCountingInterceptor();
        await using var handlerContext = _fixture.CreateContext(interceptor);
        var handler = new GetRunCockpitQueryHandler(handlerContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(new GetRunCockpitQuery(runId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        return interceptor.CommandCount;
    }
}
