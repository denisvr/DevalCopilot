using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Projects.Commands.ClaimVerificationExecution;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Tests.Features.Runs;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Projects;

/// <summary>
/// ADR-0058 at the explicit verification claim: at most two recorded potentially mutating owners exist across one database. The reproduced
/// gap (three distinct projects' production claims all persisted at b3d53bf) is a permanent regression, a competing claim that wins the
/// last place in the window between the early check and the write lock refuses the other with nothing consumed, and old over-capacity
/// facts are refused without being rewritten.
/// </summary>
public sealed class GlobalAdmissionVerificationClaimTests : IAsyncLifetime
{
    private const string LimitReached = "execution_capacity.limit_reached";
    private static readonly DateTimeOffset Now = WorkspaceExecutionSeed.Now;
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    private sealed class FixedReader(Func<Task>? onCapture = null) : IGitWorkspaceEvidenceReader
    {
        public int Captures { get; private set; }

        public async Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken)
        {
            Captures++;
            if (onCapture is not null)
            {
                await onCapture();
            }

            return new GitWorkspaceEvidenceResult(
                GitWorkspaceEvidenceOutcome.Success, new string('a', 40), WorkspaceExecutionSeed.Fingerprint, [], null);
        }
    }

    /// <summary>Holds each capture until both participants reached it, so both claims pass their early checks together and meet at the
    /// database write lock.</summary>
    private sealed class BarrierReader(Barrier barrier) : IGitWorkspaceEvidenceReader
    {
        public async Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken)
        {
            await Task.Run(() => barrier.SignalAndWait(TimeSpan.FromSeconds(20)), cancellationToken);
            return new GitWorkspaceEvidenceResult(
                GitWorkspaceEvidenceOutcome.Success, new string('a', 40), WorkspaceExecutionSeed.Fingerprint, [], null);
        }
    }

    private async Task<WorkspaceExecutionScene[]> SeedAsync(int count)
    {
        await using var db = fixture.CreateContext();
        var scenes = new List<WorkspaceExecutionScene>(count);
        for (var index = 0; index < count; index++)
        {
            scenes.Add(await WorkspaceExecutionSeed.SeedAsync(db));
        }

        return [.. scenes];
    }

    private async Task<Result<ClaimVerificationExecutionCommandResult>> ClaimAsync(WorkspaceExecutionScene scene, IGitWorkspaceEvidenceReader reader)
    {
        await using var db = fixture.CreateContext();
        return await new ClaimVerificationExecutionCommandHandler(db, reader, new FixedTimeProvider(Now)).HandleAsync(
            new ClaimVerificationExecutionCommand(scene.Project.Id, scene.Recipe.Id, scene.Checkpoint.Id), CancellationToken.None);
    }

    private async Task AssertProjectUntouchedAsync(WorkspaceExecutionScene scene)
    {
        await using var verify = fixture.CreateContext();
        Assert.Equal(0, await verify.VerificationExecutions.CountAsync(execution => execution.ProjectId == scene.Project.Id));
        Assert.Equal(
            1,
            await verify.Projects.Where(project => project.Id == scene.Project.Id).Select(project => project.NextVerificationExecutionNumber).SingleAsync());
    }

    [Fact]
    public async Task A_third_independent_workspace_verification_claim_is_refused_by_the_global_ceiling()
    {
        var scenes = await SeedAsync(3);
        var reader = new FixedReader();

        Assert.True((await ClaimAsync(scenes[0], reader)).IsSuccess);
        Assert.True((await ClaimAsync(scenes[1], reader)).IsSuccess);
        var capturesBefore = reader.Captures;
        var third = await ClaimAsync(scenes[2], reader);

        Assert.True(third.IsFailure, "The third independent workspace claim was admitted.");
        Assert.Equal(LimitReached, Assert.Single(third.Errors).Code);
        Assert.Equal(capturesBefore, reader.Captures);
        await using var verify = fixture.CreateContext();
        Assert.Equal(2, await verify.VerificationExecutions.CountAsync());
        await AssertProjectUntouchedAsync(scenes[2]);
    }

    [Fact]
    public async Task Two_verification_claims_of_different_projects_racing_for_the_last_place_admit_exactly_one()
    {
        var scenes = await SeedAsync(3);
        Assert.True((await ClaimAsync(scenes[0], new FixedReader())).IsSuccess);
        var barrier = new BarrierReader(new Barrier(2));

        var results = await Task.WhenAll(
            Task.Run(() => ClaimAsync(scenes[1], barrier)), Task.Run(() => ClaimAsync(scenes[2], barrier)));

        Assert.Single(results, result => result.IsSuccess);
        var loser = Assert.Single(results, result => result.IsFailure);
        Assert.Equal(LimitReached, Assert.Single(loser.Errors).Code);
        await using var verify = fixture.CreateContext();
        Assert.Equal(2, await verify.VerificationExecutions.CountAsync());
        var counters = await verify.Projects.Where(project => scenes.Select(scene => scene.Project.Id).Contains(project.Id))
            .Select(project => project.NextVerificationExecutionNumber).ToListAsync();
        Assert.Equal(2, counters.Count(counter => counter == 2));
        Assert.Equal(1, counters.Count(counter => counter == 1));
    }

    [Fact]
    public async Task An_owner_that_takes_the_last_place_between_the_early_check_and_the_write_lock_refuses_the_claim_and_consumes_nothing()
    {
        var scenes = await SeedAsync(3);
        Assert.True((await ClaimAsync(scenes[0], new FixedReader())).IsSuccess);
        var reader = new FixedReader(async () =>
        {
            await using var other = fixture.CreateContext();
            await WorkspaceExecutionSeed.AddAgentAttemptAsync(other, scenes[1].Run.Id, scenes[1].Workspace.Id, scenes[1].Checkpoint.Id);
        });

        var result = await ClaimAsync(scenes[2], reader);

        Assert.True(result.IsFailure);
        Assert.Equal(LimitReached, Assert.Single(result.Errors).Code);
        Assert.Equal(1, reader.Captures);
        await AssertProjectUntouchedAsync(scenes[2]);
    }

    [Fact]
    public async Task A_database_already_over_capacity_refuses_a_new_claim_and_rewrites_no_recorded_fact()
    {
        var scenes = await SeedAsync(4);
        await using (var db = fixture.CreateContext())
        {
            for (var index = 0; index < 3; index++)
            {
                await WorkspaceExecutionSeed.AddVerificationExecutionAsync(db, scenes[index]);
            }
        }

        var before = await SnapshotAsync();
        var reader = new FixedReader();

        var result = await ClaimAsync(scenes[3], reader);

        Assert.True(result.IsFailure);
        Assert.Equal(LimitReached, Assert.Single(result.Errors).Code);
        Assert.Equal(0, reader.Captures);
        Assert.Equal(before, await SnapshotAsync());
        await AssertProjectUntouchedAsync(scenes[3]);
    }

    [Fact]
    public async Task A_terminal_execution_frees_its_place_and_an_unrelated_ReadOnly_attempt_never_consumes_one()
    {
        var scenes = await SeedAsync(4);
        Guid first;
        await using (var db = fixture.CreateContext())
        {
            first = await WorkspaceExecutionSeed.AddVerificationExecutionAsync(db, scenes[0]);
            await WorkspaceExecutionSeed.AddVerificationExecutionAsync(db, scenes[1]);
        }

        Assert.True((await ClaimAsync(scenes[2], new FixedReader())).IsFailure);

        await using (var db = fixture.CreateContext())
        {
            await WorkspaceExecutionSeed.SetExecutionStatusAsync(db, first, VerificationExecutionStatus.Passed);
            await AddReadOnlyAttemptAsync(db, scenes[3]);
        }

        Assert.True((await ClaimAsync(scenes[2], new FixedReader())).IsSuccess);
    }

    private static async Task AddReadOnlyAttemptAsync(DevalCopilot.Infrastructure.Persistence.DevalCopilotDbContext db, WorkspaceExecutionScene scene)
    {
        var attempt = Attempt.ClaimAgent(
            Guid.NewGuid(), scene.Run.Id, 1, scene.Workspace.Id, scene.Checkpoint.Id, WorkspaceExecutionSeed.Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 1024, 2048, Now, 1);
        attempt.MarkAgentDispatched(Now);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync();
    }

    private sealed record Snapshot(int Attempts, int Executions, int Operations, string ExecutionStates, string Counters);

    private async Task<Snapshot> SnapshotAsync()
    {
        await using var db = fixture.CreateContext();
        var executions = await db.VerificationExecutions.AsNoTracking().Select(execution => new { execution.Id, execution.Status }).ToListAsync();
        var counters = await db.Projects.AsNoTracking().Select(project => new { project.Id, project.NextVerificationExecutionNumber }).ToListAsync();
        return new Snapshot(
            await db.Attempts.CountAsync(),
            executions.Count,
            await db.LocalCommitOperations.CountAsync(),
            string.Join(',', executions.OrderBy(item => item.Id).Select(item => $"{item.Id}:{item.Status}")),
            string.Join(',', counters.OrderBy(item => item.Id).Select(item => $"{item.Id}:{item.NextVerificationExecutionNumber}")));
    }
}
