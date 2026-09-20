using System.Data.Common;
using DevalCopilot.Application.Features.Runs.Commands.RecordReviewCorrectionInputAlreadyCorrected;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed class RecordReviewCorrectionInputAlreadyCorrectedCommandHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 13, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);
    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task Exact_same_run_identity_completes_as_input_already_corrected()
    {
        await using var context = _fixture.CreateContext();
        var seed = await SeedAsync(context, candidateRunIsSame: true, candidateCheckpointIsSame: true, candidateInputIds: [seedIds.Report, seedIds.FindingOne, seedIds.FindingTwo]);
        var handler = new RecordReviewCorrectionInputAlreadyCorrectedCommandHandler(context, new FixedTimeProvider(Now.AddMinutes(1)));

        var result = await handler.HandleAsync(new RecordReviewCorrectionInputAlreadyCorrectedCommand(seed.Run.Id, seed.Waiting.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AttemptStatus.Failed, seed.Waiting.Status);
        Assert.Equal(AgentOutcome.InputAlreadyCorrected, seed.Waiting.AgentOutcome);
        Assert.Single(context.Events.Where(item => item.AttemptId == seed.Waiting.Id));
    }

    [Theory]
    [MemberData(nameof(MismatchedCandidateCases))]
    public async Task Non_exact_identity_never_mutates_the_waiting_attempt(
        bool sameRun, bool sameCheckpoint, Guid[] candidateInputIds)
    {
        await using var context = _fixture.CreateContext();
        var seed = await SeedAsync(context, sameRun, sameCheckpoint, candidateInputIds);
        var handler = new RecordReviewCorrectionInputAlreadyCorrectedCommandHandler(context, new FixedTimeProvider(Now.AddMinutes(1)));

        var result = await handler.HandleAsync(new RecordReviewCorrectionInputAlreadyCorrectedCommand(seed.Run.Id, seed.Waiting.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.no_competing_correction_found", Assert.Single(result.Errors).Code);
        Assert.Equal(AttemptStatus.Running, seed.Waiting.Status);
        Assert.Null(seed.Waiting.AgentOutcome);
        Assert.Empty(context.Events.Where(item => item.AttemptId == seed.Waiting.Id));
    }

    public static TheoryData<bool, bool, Guid[]> MismatchedCandidateCases => new()
    {
        { true, true, [seedIds.Report, seedIds.FindingTwo, seedIds.FindingOne] },
        { true, true, [seedIds.Report, seedIds.FindingOne] },
        { true, true, [seedIds.Report, seedIds.FindingOne, Guid.Parse("00000000-0000-0000-0000-000000000099")] },
        { false, true, [seedIds.Report, seedIds.FindingOne, seedIds.FindingTwo] },
        { true, false, [seedIds.Report, seedIds.FindingOne, seedIds.FindingTwo] },
    };

    [Fact]
    public async Task Query_count_is_fixed_when_unrelated_candidates_grow()
    {
        var few = await MeasureCandidateQueryCountAsync(3);
        var many = await MeasureCandidateQueryCountAsync(30);

        Assert.Equal(few, many);
        Assert.Equal(4, few);
    }

    private async Task<int> MeasureCandidateQueryCountAsync(int candidateCount)
    {
        Guid runId;
        Guid waitingId;
        await using (var context = _fixture.CreateContext())
        {
            var seed = await SeedAsync(context, candidateRunIsSame: true, candidateCheckpointIsSame: true, candidateInputIds: [seedIds.Report, seedIds.FindingOne]);
            runId = seed.Run.Id;
            waitingId = seed.Waiting.Id;
            for (var index = 0; index < candidateCount; index++)
            {
                var candidate = Attempt.ClaimAgentReviewCorrection(Guid.NewGuid(), seed.Run.Id, index + 3, seed.Workspace.Id, seed.Checkpoint.Id, Fingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(20), 262144, 524288, Now);
                candidate.MarkAgentDispatched(Now);
                candidate.CompleteReviewCorrection(AgentOutcome.CorrectionApplied, Guid.NewGuid(), Now);
                context.Attempts.Add(candidate);
                context.AttemptInputMessages.AddRange(
                    AttemptInputMessage.Record(Guid.NewGuid(), candidate.Id, seedIds.Report, 0),
                    AttemptInputMessage.Record(Guid.NewGuid(), candidate.Id, Guid.NewGuid(), 1));
            }

            await context.SaveChangesAsync(CancellationToken.None);
        }

        var interceptor = new ReaderCountingInterceptor();
        await using var measuredContext = _fixture.CreateContext(interceptor);
        var waiting = await measuredContext.Attempts.SingleAsync(item => item.Id == waitingId && item.RunId == runId);
        interceptor.Reset();

        var handler = new RecordReviewCorrectionInputAlreadyCorrectedCommandHandler(measuredContext, new FixedTimeProvider(Now.AddMinutes(1)));
        var result = await handler.HandleAsync(new RecordReviewCorrectionInputAlreadyCorrectedCommand(waiting.RunId, waiting.Id), CancellationToken.None);
        Assert.True(result.IsFailure);
        return interceptor.Count;
    }

    private async Task<Seed> SeedAsync(DevalCopilotDbContext context, bool candidateRunIsSame, bool candidateCheckpointIsSame, IReadOnlyList<Guid> candidateInputIds)
    {
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Correct the implementation", Now);
        run.Claim(Now);
        var workspace = GitWorkspace.Prepare(Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        workspace.MarkReady();
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), Fingerprint, []);
        var lease = RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, project.Id.ToByteArray(), Now);
        var waiting = Attempt.ClaimAgentReviewCorrection(Guid.NewGuid(), run.Id, 1, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(20), 262144, 524288, Now);
        var candidateRun = candidateRunIsSame ? run : Run.RecordIntent(Guid.NewGuid(), project.Id, 2, "Foreign run", Now);
        if (!candidateRunIsSame) candidateRun.Claim(Now);
        var candidateWorkspace = candidateRunIsSame ? workspace : GitWorkspace.Prepare(Guid.NewGuid(), project.Id, 2, $@"C:\workspaces\{Guid.NewGuid():N}", "foreign", new string('a', 40), "main", Now);
        if (!candidateRunIsSame) candidateWorkspace.MarkReady();
        var candidateCheckpoint = candidateCheckpointIsSame ? checkpoint : GitCheckpoint.Capture(Guid.NewGuid(), candidateWorkspace.Id, 1, Now, new string('a', 40), new string('b', 64), []);
        var candidate = Attempt.ClaimAgentReviewCorrection(Guid.NewGuid(), candidateRun.Id, 2, candidateWorkspace.Id, candidateCheckpoint.Id, candidateCheckpoint.FingerprintSha256, Guid.NewGuid(), TimeSpan.FromMinutes(20), 262144, 524288, Now);
        candidate.MarkAgentDispatched(Now);
        candidate.CompleteReviewCorrection(AgentOutcome.CorrectionApplied, Guid.NewGuid(), Now);

        context.Projects.Add(project);
        context.Runs.Add(run);
        context.GitWorkspaces.Add(workspace);
        context.GitCheckpoints.Add(checkpoint);
        context.RepositoryMutationLeases.Add(lease);
        context.Attempts.Add(waiting);
        context.AttemptInputMessages.AddRange(
            AttemptInputMessage.Record(Guid.NewGuid(), waiting.Id, seedIds.Report, 0),
            AttemptInputMessage.Record(Guid.NewGuid(), waiting.Id, seedIds.FindingOne, 1),
            AttemptInputMessage.Record(Guid.NewGuid(), waiting.Id, seedIds.FindingTwo, 2));
        if (!candidateRunIsSame)
        {
            context.Runs.Add(candidateRun);
            context.GitWorkspaces.Add(candidateWorkspace);
            context.GitCheckpoints.Add(candidateCheckpoint);
        }
        context.Attempts.Add(candidate);
        context.AttemptInputMessages.AddRange(candidateInputIds.Select((id, index) => AttemptInputMessage.Record(Guid.NewGuid(), candidate.Id, id, index)));
        await context.SaveChangesAsync(CancellationToken.None);
        return new Seed(run, waiting, workspace, checkpoint);
    }

    private sealed record Seed(Run Run, Attempt Waiting, GitWorkspace Workspace, GitCheckpoint Checkpoint);

    private static class seedIds
    {
        public static readonly Guid Report = Guid.Parse("00000000-0000-0000-0000-000000000001");
        public static readonly Guid FindingOne = Guid.Parse("00000000-0000-0000-0000-000000000002");
        public static readonly Guid FindingTwo = Guid.Parse("00000000-0000-0000-0000-000000000003");
    }

    private sealed class ReaderCountingInterceptor : DbCommandInterceptor
    {
        public int Count { get; private set; }
        public void Reset() => Count = 0;
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result) { Count++; return result; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) { Count++; return ValueTask.FromResult(result); }
    }
}
