using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Projects.Commands.ClaimVerificationExecution;
using DevalCopilot.Application.Features.Projects.Policies;
using DevalCopilot.Application.Features.Projects.Policies.WorkspaceExecutions;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Tests.Features.Runs;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Projects;

/// <summary>
/// ADR-0057 at the explicit verification claim: an Agent attempt of the workspace (of any run), a local-commit reservation, conflicting
/// or untrustworthy ownership refuses the claim before the Git capture and again under the claim transaction's write lock, with no
/// execution recorded and no execution number consumed. Another repository and terminal history never refuse it.
/// </summary>
public sealed class ClaimVerificationExecutionWorkspaceExecutionTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = WorkspaceExecutionSeed.Now;
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    private sealed class CountingReader(Func<Task>? onCapture = null) : IGitWorkspaceEvidenceReader
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

    private async Task<Result<ClaimVerificationExecutionCommandResult>> ClaimAsync(
        WorkspaceExecutionScene scene, CountingReader reader, Func<Task>? beforeBegin = null)
    {
        await using var inner = fixture.CreateContext();
        var faulting = new FaultInjectingDbContext(inner)
        {
            BeforeBeginTransaction = _ => beforeBegin?.Invoke() ?? Task.CompletedTask,
        };
        return await new ClaimVerificationExecutionCommandHandler(faulting, reader, new FixedTimeProvider(Now)).HandleAsync(
            new ClaimVerificationExecutionCommand(scene.Project.Id, scene.Recipe.Id, scene.Checkpoint.Id), CancellationToken.None);
    }

    private async Task<WorkspaceExecutionScene> SeedAsync()
    {
        await using var db = fixture.CreateContext();
        return await WorkspaceExecutionSeed.SeedAsync(db);
    }

    private async Task AssertNothingClaimedAsync(WorkspaceExecutionScene scene, int expectedExecutions = 0)
    {
        await using var verify = fixture.CreateContext();
        Assert.Equal(expectedExecutions, await verify.VerificationExecutions.CountAsync());
        Assert.Equal(
            1,
            await verify.Projects.Where(project => project.Id == scene.Project.Id).Select(project => project.NextVerificationExecutionNumber).SingleAsync());
    }

    public static TheoryData<string, string> EarlyOwners() => new()
    {
        { "agent attempt", WorkspaceExecutionErrors.AgentAttemptActiveCode },
        { "undispatched agent attempt", WorkspaceExecutionErrors.AgentAttemptActiveCode },
        { "agent attempt of another run", WorkspaceExecutionErrors.AgentAttemptActiveCode },
        { "commit reservation", "verification.workspace_not_ready" },
    };

    [Theory]
    [MemberData(nameof(EarlyOwners))]
    public async Task A_competing_owner_refuses_the_claim_before_the_git_capture_and_consumes_no_execution_number(string owner, string code)
    {
        var scene = await SeedAsync();
        await PlantAsync(scene, owner);
        var reader = new CountingReader();

        var result = await ClaimAsync(scene, reader);

        Assert.True(result.IsFailure);
        Assert.Equal(code, Assert.Single(result.Errors).Code);
        Assert.Equal(0, reader.Captures);
        await AssertNothingClaimedAsync(scene);
    }

    [Theory]
    [InlineData("agent attempt")]
    [InlineData("undispatched agent attempt")]
    [InlineData("agent attempt of another run")]
    public async Task A_competing_owner_that_wins_during_the_capture_refuses_the_claim_under_the_write_lock(string owner)
    {
        var scene = await SeedAsync();
        var reader = new CountingReader(() => PlantAsync(scene, owner));

        var result = await ClaimAsync(scene, reader);

        Assert.True(result.IsFailure);
        Assert.Equal(WorkspaceExecutionErrors.AgentAttemptActiveCode, Assert.Single(result.Errors).Code);
        Assert.Equal(1, reader.Captures);
        await AssertNothingClaimedAsync(scene);
    }

    [Fact]
    public async Task A_competing_owner_that_wins_just_before_the_claim_transaction_refuses_the_claim_and_consumes_nothing()
    {
        var scene = await SeedAsync();
        var reader = new CountingReader();

        var result = await ClaimAsync(scene, reader, beforeBegin: () => PlantAsync(scene, "agent attempt"));

        Assert.Equal(WorkspaceExecutionErrors.AgentAttemptActiveCode, Assert.Single(result.Errors).Code);
        await AssertNothingClaimedAsync(scene);
    }

    [Fact]
    public async Task A_commit_reservation_that_wins_before_the_transaction_is_never_overridden()
    {
        var scene = await SeedAsync();

        var result = await ClaimAsync(scene, new CountingReader(), beforeBegin: () => PlantAsync(scene, "commit reservation"));

        Assert.True(result.IsFailure);
        Assert.Equal("verification.workspace_not_ready", Assert.Single(result.Errors).Code);
        await AssertNothingClaimedAsync(scene);
    }

    [Fact]
    public async Task Conflicting_owners_refuse_the_claim_without_choosing_a_winner()
    {
        var scene = await SeedAsync();
        await PlantAsync(scene, "agent attempt");
        await PlantAsync(scene, "agent attempt of another run");

        var result = await ClaimAsync(scene, new CountingReader());

        Assert.Equal(WorkspaceExecutionErrors.OwnersConflictedCode, Assert.Single(result.Errors).Code);
        await AssertNothingClaimedAsync(scene);
        await using var verify = fixture.CreateContext();
        Assert.Equal(2, await verify.Attempts.CountAsync(attempt => attempt.Status == AttemptStatus.Running));
    }

    [Fact]
    public async Task An_Agent_attempt_of_the_project_that_names_no_workspace_refuses_the_claim_as_unavailable()
    {
        var scene = await SeedAsync();
        await PlantAsync(scene, "agent attempt of another run");
        await using (var db = fixture.CreateContext())
        {
            await db.Database.ExecuteSqlRawAsync("UPDATE attempts SET AgentGitWorkspaceId = NULL");
        }

        var result = await ClaimAsync(scene, new CountingReader());

        Assert.Equal(WorkspaceExecutionErrors.UnavailableCode, Assert.Single(result.Errors).Code);
        await AssertNothingClaimedAsync(scene);
    }

    [Fact]
    public async Task A_verification_of_the_project_naming_a_workspace_of_another_project_refuses_the_claim_as_unavailable()
    {
        var scene = await SeedAsync();
        var foreign = await SeedAsync();
        Guid executionId;
        await using (var db = fixture.CreateContext())
        {
            executionId = await WorkspaceExecutionSeed.AddVerificationExecutionAsync(db, scene);
            await db.VerificationExecutions.Where(candidate => candidate.Id == executionId)
                .ExecuteUpdateAsync(set => set.SetProperty(candidate => candidate.GitWorkspaceId, foreign.Workspace.Id));
        }

        var reader = new CountingReader();
        var result = await ClaimAsync(scene, reader);

        Assert.True(result.IsFailure);
        Assert.Equal(WorkspaceExecutionErrors.UnavailableCode, Assert.Single(result.Errors).Code);
        Assert.Equal(0, reader.Captures);
        await using var verify = fixture.CreateContext();
        Assert.Equal(1, await verify.VerificationExecutions.CountAsync(candidate => candidate.ProjectId == scene.Project.Id));
    }

    [Fact]
    public async Task Terminal_history_and_another_repository_never_refuse_the_claim()
    {
        var scene = await SeedAsync();
        var unrelated = await SeedAsync();
        await using (var db = fixture.CreateContext())
        {
            var finished = await WorkspaceExecutionSeed.AddAgentAttemptAsync(db, scene.Run.Id, scene.Workspace.Id, scene.Checkpoint.Id);
            await WorkspaceExecutionSeed.SetAttemptStatusAsync(db, finished, AttemptStatus.Interrupted);
            await WorkspaceExecutionSeed.AddAgentAttemptAsync(db, unrelated.Run.Id, unrelated.Workspace.Id, unrelated.Checkpoint.Id);
        }

        var result = await ClaimAsync(scene, new CountingReader());

        Assert.True(result.IsSuccess);
        await using var verify = fixture.CreateContext();
        Assert.Equal(WorkspaceExecutionStatus.VerificationActive, (await WorkspaceExecutionPolicy.AssessAsync(
            verify, scene.Project.Id, scene.Workspace.Id, null, null, CancellationToken.None)).Status);
    }

    private async Task PlantAsync(WorkspaceExecutionScene scene, string owner)
    {
        await using var db = fixture.CreateContext();
        if (owner == "commit reservation")
        {
            await WorkspaceExecutionSeed.SetWorkspaceStatusAsync(db, scene.Workspace.Id, WorkspaceStatus.Committing);
            return;
        }

        var runId = scene.Run.Id;
        var number = 1;
        if (owner == "agent attempt of another run")
        {
            var other = await WorkspaceExecutionSeed.AddRunAsync(db, scene.Project.Id, 2 + await db.Runs.CountAsync(run => run.ProjectId == scene.Project.Id));
            runId = other.Id;
        }
        else
        {
            number = 1 + await db.Attempts.CountAsync(attempt => attempt.RunId == runId);
        }

        await WorkspaceExecutionSeed.AddAgentAttemptAsync(
            db, runId, scene.Workspace.Id, scene.Checkpoint.Id, dispatched: owner != "undispatched agent attempt", attemptNumber: number);
    }
}
