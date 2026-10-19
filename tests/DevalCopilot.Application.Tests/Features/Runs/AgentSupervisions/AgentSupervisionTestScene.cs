using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Commands.AdvanceAgentSupervisionPhase;
using DevalCopilot.Application.Features.Runs.Commands.CloseAgentSupervision;
using DevalCopilot.Application.Features.Runs.Commands.CloseInterruptedAgentSupervisions;
using DevalCopilot.Application.Features.Runs.Commands.HeartbeatAgentSupervision;
using DevalCopilot.Application.Features.Runs.Commands.ObserveAgentSupervision;
using DevalCopilot.Application.Features.Runs.Commands.RegisterAgentSupervision;
using DevalCopilot.Application.Features.Runs.Policies.AgentSupervisions;
using DevalCopilot.Application.Features.Runs.Queries.GetAgentSupervision;
using DevalCopilot.Application.Features.Runs.Queries.GetOpenAgentSupervisions;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.AgentSupervisions;

/// <summary>
/// One ManualAgent run with a Ready, owned workspace and its latest checkpoint over a real file-backed SQLite database with production
/// migrations (ADR-0056). Any of the seven Agent response contracts is claimed as a real Running Agent attempt through the Domain
/// factories, and every handler is built over its own independent <see cref="DevalCopilot.Infrastructure.Persistence.DevalCopilotDbContext"/>
/// so the races between commits are real SQLite races. The clock is shared and adjustable; nothing here starts a provider.
/// </summary>
internal sealed class AgentSupervisionTestScene
{
    public static readonly DateTimeOffset Start = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);

    private int nextAttemptNumber = 1;

    private AgentSupervisionTestScene(SqliteDatabaseFixture fixture, Guid runId, Guid workspaceId, Guid checkpointId)
    {
        Fixture = fixture;
        RunId = runId;
        WorkspaceId = workspaceId;
        CheckpointId = checkpointId;
    }

    public SqliteDatabaseFixture Fixture { get; }

    public Guid RunId { get; }

    public Guid WorkspaceId { get; }

    public Guid CheckpointId { get; }

    public AdjustableTimeProvider Clock { get; } = new(Start);

    public AgentSupervisionHostInstance Host { get; } = new(Guid.NewGuid());

    public static async Task<AgentSupervisionTestScene> CreateAsync(SqliteDatabaseFixture fixture)
    {
        await using var context = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Start);
        var run = Run.RecordClassifiedIntent(
            Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), RunExecutionMode.ManualAgent, "Observe the host", Start);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Start);
        workspace.MarkReady();
        Assert.Equal(1, workspace.ReserveCheckpointNumber());
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Start, new string('a', 40), Fingerprint, []);
        context.Projects.Add(project);
        context.Runs.Add(run);
        context.GitWorkspaces.Add(workspace);
        context.GitCheckpoints.Add(checkpoint);
        await context.SaveChangesAsync(CancellationToken.None);
        return new AgentSupervisionTestScene(fixture, run.Id, workspace.Id, checkpoint.Id);
    }

    /// <summary>Claims one real, still Running Agent attempt of the given contract with its reserved invocation timeout.</summary>
    public async Task<Guid> ClaimAsync(AgentResponseContract contract = AgentResponseContract.Proposal, TimeSpan? timeout = null)
    {
        await using var context = Fixture.CreateContext();
        var number = nextAttemptNumber++;
        var manifestId = Guid.NewGuid();
        var attemptTimeout = timeout ?? TimeSpan.FromMinutes(10);
        var attempt = ClaimOf(contract, Guid.NewGuid(), number, manifestId, attemptTimeout);
        context.Attempts.Add(attempt);
        context.Artifacts.Add(Artifact.Record(
            manifestId, RunId, attempt.Id, ArtifactPurpose.AgentContextManifest, "application/json",
            $"runs/{RunId}/attempts/{attempt.Id}/manifest.sealed", "sha256:manifest", 256, false,
            ArtifactCaptureOutcome.Captured, ArtifactSensitivity.HostConstructedContent,
            ArtifactRetentionPolicy.RetainUntilRunDeleted, Start));
        await context.SaveChangesAsync(CancellationToken.None);
        return attempt.Id;
    }

    /// <summary>A Simulated attempt: never observable.</summary>
    public async Task<Guid> ClaimSimulatedAsync()
    {
        await using var context = Fixture.CreateContext();
        var attempt = Attempt.Claim(Guid.NewGuid(), RunId, nextAttemptNumber++, Start);
        context.Attempts.Add(attempt);
        await context.SaveChangesAsync(CancellationToken.None);
        return attempt.Id;
    }

    /// <summary>Makes the attempt terminal at the scene clock through the Domain transitions (an ordinary result recording).</summary>
    public async Task CompleteAsync(Guid attemptId, AttemptStatus status = AttemptStatus.Completed)
    {
        await using var context = Fixture.CreateContext();
        var attempt = await context.Attempts.SingleAsync(candidate => candidate.Id == attemptId);
        switch (status)
        {
            case AttemptStatus.Completed:
                attempt.Complete(Clock.GetUtcNow());
                break;
            case AttemptStatus.Failed:
                attempt.Fail(Clock.GetUtcNow());
                break;
            default:
                attempt.Interrupt(Clock.GetUtcNow());
                break;
        }

        await context.SaveChangesAsync(CancellationToken.None);
    }

    public async Task<Result<RegisterAgentSupervisionCommandResult>> RegisterAsync(
        Guid attemptId, Guid observerId, Guid? hostInstanceId = null, Guid? runId = null)
    {
        await using var context = Fixture.CreateContext();
        return await new RegisterAgentSupervisionCommandHandler(context, Clock, Host).HandleAsync(
            new RegisterAgentSupervisionCommand(runId ?? RunId, attemptId, hostInstanceId ?? Host.Id, observerId), CancellationToken.None);
    }

    /// <summary>Registers through the production handler and returns the observer that owns the observation.</summary>
    public async Task<Guid> ObserveAttemptAsync(Guid attemptId)
    {
        var observer = Guid.NewGuid();
        var result = await RegisterAsync(attemptId, observer);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
        return observer;
    }

    public async Task<Result<HeartbeatAgentSupervisionCommandResult>> HeartbeatAsync(
        Guid attemptId, Guid observerId, Guid? hostInstanceId = null)
    {
        await using var context = Fixture.CreateContext();
        return await new HeartbeatAgentSupervisionCommandHandler(context, Clock, Host).HandleAsync(
            new HeartbeatAgentSupervisionCommand(attemptId, hostInstanceId ?? Host.Id, observerId), CancellationToken.None);
    }

    public async Task<Result<AdvanceAgentSupervisionPhaseCommandResult>> AdvanceAsync(
        Guid attemptId, Guid observerId, AgentSupervisionPhase phase, Guid? hostInstanceId = null)
    {
        await using var context = Fixture.CreateContext();
        return await new AdvanceAgentSupervisionPhaseCommandHandler(context, Clock, Host).HandleAsync(
            new AdvanceAgentSupervisionPhaseCommand(attemptId, hostInstanceId ?? Host.Id, observerId, phase), CancellationToken.None);
    }

    public async Task<Result<CloseAgentSupervisionCommandResult>> CloseAsync(
        Guid attemptId, Guid observerId, Guid? hostInstanceId = null)
    {
        await using var context = Fixture.CreateContext();
        return await new CloseAgentSupervisionCommandHandler(context, Clock, Host).HandleAsync(
            new CloseAgentSupervisionCommand(attemptId, hostInstanceId ?? Host.Id, observerId), CancellationToken.None);
    }

    public async Task<Result<ObserveAgentSupervisionCommandResult>> SweepAsync(Guid supervisionId, AgentSupervisionHostInstance? host = null)
    {
        await using var context = Fixture.CreateContext();
        return await new ObserveAgentSupervisionCommandHandler(context, Clock, host ?? Host).HandleAsync(
            new ObserveAgentSupervisionCommand(supervisionId), CancellationToken.None);
    }

    public async Task<Result<CloseInterruptedAgentSupervisionsCommandResult>> CloseInterruptedAsync(AgentSupervisionHostInstance? host = null)
    {
        await using var context = Fixture.CreateContext();
        return await new CloseInterruptedAgentSupervisionsCommandHandler(context, Clock, host ?? Host).HandleAsync(
            new CloseInterruptedAgentSupervisionsCommand(), CancellationToken.None);
    }

    public async Task<OpenAgentSupervisionWindow> OpenWindowAsync(Guid? after)
    {
        await using var context = Fixture.CreateContext();
        return await new GetOpenAgentSupervisionsQueryHandler(context).HandleAsync(
            new GetOpenAgentSupervisionsQuery(after), CancellationToken.None);
    }

    public async Task<Result<GetAgentSupervisionQueryResult>> ReadAsync(Guid? runId = null, AgentSupervisionHostInstance? host = null)
    {
        await using var context = Fixture.CreateContext();
        return await new GetAgentSupervisionQueryHandler(context, Clock, host ?? Host).HandleAsync(
            new GetAgentSupervisionQuery(runId ?? RunId), CancellationToken.None);
    }

    public async Task<GetAgentSupervisionQueryResult> ReadValueAsync()
    {
        var result = await ReadAsync();
        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
        return result.Value;
    }

    public async Task<AgentSupervision> ReceiptAsync(Guid attemptId)
    {
        await using var context = Fixture.CreateContext();
        return await context.AgentSupervisions.AsNoTracking().SingleAsync(candidate => candidate.AttemptId == attemptId);
    }

    public async Task<List<RunEvent>> SupervisionEventsAsync()
    {
        await using var context = Fixture.CreateContext();
        return (await context.Events.AsNoTracking().Where(candidate => candidate.RunId == RunId).ToListAsync())
            .Where(candidate => candidate.EventType.StartsWith("agent_supervision.", StringComparison.Ordinal))
            .OrderBy(candidate => candidate.Sequence)
            .ToList();
    }

    /// <summary>The supervision event types of the run, in commit order.</summary>
    public async Task<List<string>> SupervisionEventTypesAsync() =>
        (await SupervisionEventsAsync()).Select(runEvent => runEvent.EventType).ToList();

    public async Task<(int Attempts, int Events, int Runs, string Lifecycle)> FactsAsync()
    {
        await using var context = Fixture.CreateContext();
        var run = await context.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == RunId);
        return (await context.Attempts.CountAsync(candidate => candidate.RunId == RunId),
            await context.Events.CountAsync(candidate => candidate.RunId == RunId), await context.Runs.CountAsync(), run.Lifecycle.ToString());
    }

    public async Task<(AttemptStatus Status, AgentOutcome? Outcome, DateTimeOffset? Completed, DateTimeOffset? Dispatched)> AttemptFactsAsync(Guid attemptId)
    {
        await using var context = Fixture.CreateContext();
        var attempt = await context.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attemptId);
        return (attempt.Status, attempt.AgentOutcome, attempt.CompletedAtUtc, attempt.AgentDispatchedAtUtc);
    }

    private Attempt ClaimOf(AgentResponseContract contract, Guid id, int number, Guid manifestId, TimeSpan timeout)
    {
        const int Bytes = 262144;
        const int Total = 524288;
        return contract switch
        {
            AgentResponseContract.Proposal => Attempt.ClaimAgent(
                id, RunId, number, WorkspaceId, CheckpointId, Fingerprint, manifestId, timeout, Bytes, Total, Start, number),
            AgentResponseContract.CriticalReview => Attempt.ClaimAgentCriticalReview(
                id, RunId, number, WorkspaceId, CheckpointId, Fingerprint, manifestId, timeout, Bytes, Total, Start, number),
            AgentResponseContract.ChallengeResolution => Attempt.ClaimAgentChallengeResolution(
                id, RunId, number, WorkspaceId, CheckpointId, Fingerprint, manifestId, timeout, Bytes, Total, Start, number),
            AgentResponseContract.ImplementationReport => Attempt.ClaimAgentImplementation(
                id, RunId, number, WorkspaceId, CheckpointId, Fingerprint, manifestId, timeout, Bytes, Total, Start, number),
            AgentResponseContract.ImplementationReview => Attempt.ClaimAgentCodeReview(
                id, RunId, number, WorkspaceId, CheckpointId, Fingerprint, manifestId, timeout, Bytes, Total, Start, number),
            AgentResponseContract.ReviewCorrection => Attempt.ClaimAgentReviewCorrection(
                id, RunId, number, WorkspaceId, CheckpointId, Fingerprint, manifestId, timeout, Bytes, Total, Start, number),
            AgentResponseContract.VerificationDiagnosis => Attempt.ClaimAgentVerificationDiagnosis(
                id, RunId, number, WorkspaceId, CheckpointId, Fingerprint, manifestId, timeout, Bytes, Total, Start, null, null, number),
            _ => throw new ArgumentOutOfRangeException(nameof(contract)),
        };
    }
}
