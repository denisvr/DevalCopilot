using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed class MarkAgentAttemptDispatchedCommandHandlerReviewCorrectionTests(SqliteDatabaseFixture fixture)
    : IClassFixture<SqliteDatabaseFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 13, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);

    [Fact]
    public async Task HandleAsync_rejects_dispatch_for_an_exact_successful_correction_input()
    {
        await using var dbContext = fixture.CreateContext();
        var seed = await SeedAsync(dbContext, [1, 2]);
        var handler = new MarkAgentAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new MarkAgentAttemptDispatchedCommand(seed.Run.Id, seed.WaitingAttempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(MarkAgentAttemptDispatchedCommandHandler.InputAlreadyCorrectedCode, Assert.Single(result.Errors).Code);
        Assert.Null(seed.WaitingAttempt.AgentDispatchedAtUtc);
    }

    [Fact]
    public async Task HandleAsync_dispatches_when_a_competing_correction_is_reordered_or_partial()
    {
        await using var dbContext = fixture.CreateContext();
        var seed = await SeedAsync(dbContext, [2]);
        var handler = new MarkAgentAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new MarkAgentAttemptDispatchedCommand(seed.Run.Id, seed.WaitingAttempt.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(seed.WaitingAttempt.AgentDispatchedAtUtc);
    }

    private async Task<Seed> SeedAsync(DevalCopilotDbContext dbContext, IReadOnlyList<int> competingInputOrder)
    {
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Correct the implementation", Now);
        run.Claim(Now);
        var workspace = GitWorkspace.Prepare(Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        workspace.MarkReady();
        var physicalIdentity = project.Id.ToByteArray();
        var lease = RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, BitConverter.ToUInt64(physicalIdentity), physicalIdentity, Now);
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), Fingerprint, []);
        var waiting = Attempt.ClaimAgentReviewCorrection(Guid.NewGuid(), run.Id, 1, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(20), 262144, 524288, Now);
        var competing = Attempt.ClaimAgentReviewCorrection(Guid.NewGuid(), run.Id, 2, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(20), 262144, 524288, Now);
        competing.MarkAgentDispatched(Now);
        competing.CompleteReviewCorrection(AgentOutcome.CorrectionApplied, Guid.NewGuid(), Now);

        var reportId = Guid.NewGuid();
        var findingOneId = Guid.NewGuid();
        var findingTwoId = Guid.NewGuid();
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.GitWorkspaces.Add(workspace);
        dbContext.RepositoryMutationLeases.Add(lease);
        dbContext.GitCheckpoints.Add(checkpoint);
        dbContext.Attempts.AddRange(waiting, competing);
        dbContext.AttemptInputMessages.AddRange(
            AttemptInputMessage.Record(Guid.NewGuid(), waiting.Id, reportId, 0),
            AttemptInputMessage.Record(Guid.NewGuid(), waiting.Id, findingOneId, 1),
            AttemptInputMessage.Record(Guid.NewGuid(), waiting.Id, findingTwoId, 2),
            AttemptInputMessage.Record(Guid.NewGuid(), competing.Id, reportId, 0));
        for (var index = 0; index < competingInputOrder.Count; index++)
        {
            var messageId = competingInputOrder[index] == 1 ? findingOneId : findingTwoId;
            dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), competing.Id, messageId, index + 1));
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);
        return new Seed(run, waiting);
    }

    private sealed record Seed(Run Run, Attempt WaitingAttempt);
}
