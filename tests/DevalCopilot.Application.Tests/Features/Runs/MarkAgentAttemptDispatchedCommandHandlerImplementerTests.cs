using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>Mirrors the Resolver-branch coverage in <c>MarkAgentAttemptDispatchedCommandHandlerTests</c>,
/// one level further down the collaboration protocol: the Implementer dispatch-time gate compares
/// only the plan's sequence-0 Proposal id plus the starting checkpoint id, never the full ordered
/// input set — a resolved plan's identity is single-message by construction.</summary>
public sealed class MarkAgentAttemptDispatchedCommandHandlerImplementerTests(SqliteDatabaseFixture fixture)
    : IClassFixture<SqliteDatabaseFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 10, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);

    private async Task<(Run Run, Attempt Attempt)> SeedImplementerAttemptWithCompetingImplementationAsync(
        DevalCopilotDbContext dbContext, bool competingSharesSameCheckpoint)
    {
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Implement the resolved plan", Now);
        run.Claim(Now);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        workspace.MarkReady();
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), Fingerprint, []);
        var lease = RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, Guid.NewGuid().ToByteArray(), Now);

        var attempt = Attempt.ClaimAgentImplementation(
            Guid.NewGuid(), run.Id, 1, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(20), 262144, 524288, Now, 1);

        var planProposalMessageId = Guid.NewGuid();

        var competingCheckpointId = competingSharesSameCheckpoint ? checkpoint.Id : Guid.NewGuid();
        var competingImplementation = Attempt.ClaimAgentImplementation(
            Guid.NewGuid(), run.Id, 2, workspace.Id, competingCheckpointId, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(20), 262144, 524288, Now, 2);
        competingImplementation.MarkAgentDispatched(Now);
        competingImplementation.CompleteImplementation(AgentOutcome.Implemented, Guid.NewGuid(), Now, processEvidence: TestProcessEvidence.CleanExit);

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.GitWorkspaces.Add(workspace);
        dbContext.GitCheckpoints.Add(checkpoint);
        dbContext.RepositoryMutationLeases.Add(lease);
        dbContext.Attempts.AddRange(attempt, competingImplementation);
        dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, planProposalMessageId, sequence: 0));
        dbContext.AttemptInputMessages.Add(
            AttemptInputMessage.Record(Guid.NewGuid(), competingImplementation.Id, planProposalMessageId, sequence: 0));

        await dbContext.SaveChangesAsync(CancellationToken.None);

        return (run, attempt);
    }

    [Fact]
    public async Task HandleAsync_rejects_dispatch_when_the_exact_same_plan_and_checkpoint_already_has_a_successful_implementation()
    {
        await using var dbContext = fixture.CreateContext();
        var (run, attempt) = await SeedImplementerAttemptWithCompetingImplementationAsync(dbContext, competingSharesSameCheckpoint: true);

        var handler = new MarkAgentAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(new MarkAgentAttemptDispatchedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(MarkAgentAttemptDispatchedCommandHandler.InputAlreadyImplementedCode, Assert.Single(result.Errors).Code);
        Assert.Null(attempt.AgentDispatchedAtUtc);
    }

    [Fact]
    public async Task HandleAsync_dispatches_normally_when_the_competing_implementation_was_against_a_different_checkpoint()
    {
        await using var dbContext = fixture.CreateContext();
        var (run, attempt) = await SeedImplementerAttemptWithCompetingImplementationAsync(dbContext, competingSharesSameCheckpoint: false);

        var handler = new MarkAgentAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(new MarkAgentAttemptDispatchedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(attempt.AgentDispatchedAtUtc);
    }
}
