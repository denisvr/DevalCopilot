using DevalCopilot.Application.Features.Runs.Queries.GetIneligibleAgentAttempts;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// The complement of <c>GetEligibleAgentAttemptsQueryHandlerTests</c>: owns a fresh database per
/// test method rather than a shared <see cref="IClassFixture{T}"/>, since this query scans every
/// Agent attempt with no per-run scoping.
/// </summary>
public sealed class GetIneligibleAgentAttemptsQueryHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 18, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<(Project Project, Run Run, GitWorkspace Workspace, Attempt Attempt)> SeedCandidateAttemptAsync(
        DevalCopilotDbContext dbContext,
        bool workspaceReady = true,
        bool leaseActive = true,
        bool checkpointIsCurrent = true)
    {
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Plan the next increment", Now);
        run.Claim(Now);

        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        if (workspaceReady)
        {
            workspace.MarkReady();
        }

        var checkpointId = Guid.NewGuid();
        var attempt = Attempt.ClaimAgent(
            Guid.NewGuid(), run.Id, 1, workspace.Id, checkpointId, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.GitWorkspaces.Add(workspace);
        dbContext.Attempts.Add(attempt);

        dbContext.GitCheckpoints.Add(GitCheckpoint.Capture(checkpointId, workspace.Id, 1, Now, new string('a', 40), Fingerprint, []));
        if (!checkpointIsCurrent)
        {
            // A newer checkpoint superseded the one this attempt claimed — the workspace moved
            // on before this attempt was ever dispatched.
            dbContext.GitCheckpoints.Add(
                GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 2, Now, new string('b', 40), Fingerprint, []));
        }

        var lease = RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, new byte[16], Now);
        if (!leaseActive)
        {
            lease.Release(Now);
        }

        dbContext.RepositoryMutationLeases.Add(lease);

        await dbContext.SaveChangesAsync(CancellationToken.None);

        return (project, run, workspace, attempt);
    }

    [Fact]
    public async Task HandleAsync_returns_nothing_when_every_undispatched_agent_attempt_is_eligible()
    {
        await using var dbContext = _fixture.CreateContext();
        await SeedCandidateAttemptAsync(dbContext);

        var handler = new GetIneligibleAgentAttemptsQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetIneligibleAgentAttemptsQuery(), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task HandleAsync_returns_the_attempt_when_its_workspace_is_not_ready()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, _, attempt) = await SeedCandidateAttemptAsync(dbContext, workspaceReady: false);

        var handler = new GetIneligibleAgentAttemptsQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetIneligibleAgentAttemptsQuery(), CancellationToken.None);

        var only = Assert.Single(result);
        Assert.Equal(run.Id, only.RunId);
        Assert.Equal(attempt.Id, only.AttemptId);
    }

    [Fact]
    public async Task HandleAsync_returns_the_attempt_when_no_mutation_lease_is_active()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, _, attempt) = await SeedCandidateAttemptAsync(dbContext, leaseActive: false);

        var handler = new GetIneligibleAgentAttemptsQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetIneligibleAgentAttemptsQuery(), CancellationToken.None);

        var only = Assert.Single(result);
        Assert.Equal(run.Id, only.RunId);
        Assert.Equal(attempt.Id, only.AttemptId);
    }

    [Fact]
    public async Task HandleAsync_returns_the_attempt_when_its_claimed_checkpoint_is_no_longer_current()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, _, attempt) = await SeedCandidateAttemptAsync(dbContext, checkpointIsCurrent: false);

        var handler = new GetIneligibleAgentAttemptsQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetIneligibleAgentAttemptsQuery(), CancellationToken.None);

        var only = Assert.Single(result);
        Assert.Equal(run.Id, only.RunId);
        Assert.Equal(attempt.Id, only.AttemptId);
    }

    [Fact]
    public async Task HandleAsync_ignores_an_attempt_that_has_already_been_dispatched()
    {
        await using var dbContext = _fixture.CreateContext();
        // Otherwise ineligible on every gate — still must never be reported, since it was
        // already handed to the provider.
        var (_, _, _, attempt) = await SeedCandidateAttemptAsync(dbContext, workspaceReady: false, leaseActive: false);
        attempt.MarkAgentDispatched(Now);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetIneligibleAgentAttemptsQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetIneligibleAgentAttemptsQuery(), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task HandleAsync_ignores_an_already_terminal_agent_attempt()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, _, _, attempt) = await SeedCandidateAttemptAsync(dbContext, workspaceReady: false);
        attempt.Fail(Now);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetIneligibleAgentAttemptsQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetIneligibleAgentAttemptsQuery(), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task HandleAsync_ignores_a_non_agent_attempt()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Simulated run", Now);
        run.Claim(Now);
        var attempt = Attempt.Claim(Guid.NewGuid(), run.Id, 1, Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetIneligibleAgentAttemptsQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetIneligibleAgentAttemptsQuery(), CancellationToken.None);

        Assert.Empty(result);
    }
}
