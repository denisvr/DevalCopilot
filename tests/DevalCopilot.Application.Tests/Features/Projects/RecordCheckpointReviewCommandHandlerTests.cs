using DevalCopilot.Application.Features.Projects.Commands.RecordCheckpointReview;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Projects.Queries.GetProjectCheckpointReviews;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Infrastructure.Persistence;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Projects;

public sealed class RecordCheckpointReviewCommandHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    [Fact]
    public async Task Records_append_only_review_facts_and_allows_pending_without_execution_evidence()
    {
        await using var dbContext = fixture.CreateContext();
        var data = await AddReadyEvidenceAsync(dbContext, VerificationExecutionStatus.Interrupted, null, null);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordCheckpointReviewCommandHandler(
            dbContext, new FixedEvidenceReader(data.Checkpoint.FingerprintSha256), new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(new RecordCheckpointReviewCommand(
            data.Project.Id, data.Checkpoint.Id, data.Execution.Id, ReviewActorKind.Human, ReviewDecision.Escalated), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var review = Assert.Single(dbContext.CheckpointReviews);
        var evidence = Assert.Single(review.Evidence);
        Assert.Equal(data.Execution.Id, evidence.VerificationExecutionId);
        Assert.Equal(VerificationExecutionStatus.Interrupted, evidence.VerificationExecutionStatus);
        Assert.Null(evidence.VerificationExecutionOutcome);

        var second = await handler.HandleAsync(new RecordCheckpointReviewCommand(
            data.Project.Id, data.Checkpoint.Id, null, ReviewActorKind.FutureAgent, ReviewDecision.Pending), CancellationToken.None);
        Assert.True(second.IsSuccess);
        Assert.Equal(2, dbContext.CheckpointReviews.Count());
        var persistedReviews = dbContext.CheckpointReviews.ToArray();
        Assert.Contains(persistedReviews, review => review.Decision == ReviewDecision.Pending && review.Evidence.Count == 0);
    }

    [Fact]
    public async Task Rejects_source_drift_before_persisting_a_review()
    {
        await using var dbContext = fixture.CreateContext();
        var data = await AddReadyEvidenceAsync(dbContext, VerificationExecutionStatus.Passed, VerificationExecutionOutcome.Exited, 0);
        var handler = new RecordCheckpointReviewCommandHandler(
            dbContext, new FixedEvidenceReader(new string('c', 64)), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new RecordCheckpointReviewCommand(
            data.Project.Id, data.Checkpoint.Id, data.Execution.Id, ReviewActorKind.Human, ReviewDecision.Approved), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("reviews.checkpoint_not_current", result.Errors[0].Code);
        Assert.Empty(dbContext.CheckpointReviews);
    }

    [Fact]
    public async Task Approval_requires_a_passed_terminal_execution()
    {
        await using var dbContext = fixture.CreateContext();
        var data = await AddReadyEvidenceAsync(dbContext, VerificationExecutionStatus.Failed, VerificationExecutionOutcome.Exited, 1);
        var handler = new RecordCheckpointReviewCommandHandler(
            dbContext, new FixedEvidenceReader(data.Checkpoint.FingerprintSha256), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new RecordCheckpointReviewCommand(
            data.Project.Id, data.Checkpoint.Id, data.Execution.Id, ReviewActorKind.Human, ReviewDecision.Approved), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("reviews.approval_requires_passed_verification", result.Errors[0].Code);
    }

    [Fact]
    public async Task Every_terminal_execution_state_is_usable_for_changes_requested_or_escalated()
    {
        var scenarios = new[]
        {
            (VerificationExecutionStatus.Interrupted, (VerificationExecutionOutcome?)null, (int?)null),
            (VerificationExecutionStatus.SourceChanged, (VerificationExecutionOutcome?)null, (int?)null),
            (VerificationExecutionStatus.SourceChanged, VerificationExecutionOutcome.Exited, (int?)0),
            (VerificationExecutionStatus.Passed, VerificationExecutionOutcome.Exited, (int?)0),
            (VerificationExecutionStatus.Failed, VerificationExecutionOutcome.Exited, (int?)1),
            (VerificationExecutionStatus.TimedOut, VerificationExecutionOutcome.TimedOut, (int?)null),
            (VerificationExecutionStatus.Cancelled, VerificationExecutionOutcome.Cancelled, (int?)null),
        };

        foreach (var (status, outcome, exitCode) in scenarios)
        {
            await using var dbContext = fixture.CreateContext();
            var data = await AddReadyEvidenceAsync(dbContext, status, outcome, exitCode);
            var handler = new RecordCheckpointReviewCommandHandler(
                dbContext, new FixedEvidenceReader(data.Checkpoint.FingerprintSha256), new FixedTimeProvider(Now));

            var result = await handler.HandleAsync(new RecordCheckpointReviewCommand(
                data.Project.Id,
                data.Checkpoint.Id,
                data.Execution.Id,
                ReviewActorKind.Human,
                status == VerificationExecutionStatus.Passed ? ReviewDecision.ChangesRequested : ReviewDecision.Escalated),
                CancellationToken.None);

            Assert.True(result.IsSuccess);
        }
    }

    [Fact]
    public async Task Pending_with_execution_id_is_rejected_before_external_capture_and_mutation()
    {
        await using var dbContext = fixture.CreateContext();
        var data = await AddReadyEvidenceAsync(dbContext, VerificationExecutionStatus.Passed, VerificationExecutionOutcome.Exited, 0);
        var evidenceReader = new CountingEvidenceReader(data.Checkpoint.FingerprintSha256);
        var handler = new RecordCheckpointReviewCommandHandler(dbContext, evidenceReader, new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new RecordCheckpointReviewCommand(
            data.Project.Id, data.Checkpoint.Id, data.Execution.Id, ReviewActorKind.Human, ReviewDecision.Pending), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("reviews.pending_cannot_include_evidence", result.Errors[0].Code);
        Assert.Equal(0, evidenceReader.CaptureCount);
        Assert.Empty(dbContext.CheckpointReviews);
    }

    [Fact]
    public async Task Cross_project_evidence_is_rejected_without_mutation()
    {
        await using var dbContext = fixture.CreateContext();
        var data = await AddReadyEvidenceAsync(dbContext, VerificationExecutionStatus.Passed, VerificationExecutionOutcome.Exited, 0);
        var otherProject = Project.Register(Guid.NewGuid(), "Other project", $@"C:\repos\{Guid.NewGuid():N}", Now);
        dbContext.Projects.Add(otherProject);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new RecordCheckpointReviewCommandHandler(
            dbContext, new FixedEvidenceReader(data.Checkpoint.FingerprintSha256), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new RecordCheckpointReviewCommand(
            otherProject.Id, data.Checkpoint.Id, data.Execution.Id, ReviewActorKind.Human, ReviewDecision.Approved), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("reviews.not_found", result.Errors[0].Code);
        Assert.Empty(dbContext.CheckpointReviews);
    }

    [Fact]
    public async Task Historical_checkpoint_is_rejected_when_a_newer_checkpoint_exists()
    {
        await using var dbContext = fixture.CreateContext();
        var data = await AddReadyEvidenceAsync(dbContext, VerificationExecutionStatus.Passed, VerificationExecutionOutcome.Exited, 0);
        dbContext.GitCheckpoints.Add(GitCheckpoint.Capture(
            Guid.NewGuid(), data.Workspace.Id, 2, Now.AddMinutes(1), new string('a', 40), new string('c', 64), []));
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new RecordCheckpointReviewCommandHandler(
            dbContext, new CountingEvidenceReader(data.Checkpoint.FingerprintSha256), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new RecordCheckpointReviewCommand(
            data.Project.Id, data.Checkpoint.Id, data.Execution.Id, ReviewActorKind.Human, ReviewDecision.Approved), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("reviews.checkpoint_not_current", result.Errors[0].Code);
        Assert.Empty(dbContext.CheckpointReviews);
    }

    [Fact]
    public async Task Evidence_from_an_older_workspace_is_rejected_without_mutation()
    {
        await using var dbContext = fixture.CreateContext();
        var data = await AddReadyEvidenceAsync(dbContext, VerificationExecutionStatus.Passed, VerificationExecutionOutcome.Exited, 0);
        var newerWorkspace = GitWorkspace.Prepare(
            Guid.NewGuid(), data.Project.Id, 2, $@"C:\workspaces\{Guid.NewGuid():N}", "branch-2", new string('a', 40), "main", Now);
        newerWorkspace.MarkReady();
        dbContext.GitWorkspaces.Add(newerWorkspace);
        dbContext.RepositoryMutationLeases.Add(RepositoryMutationLease.Acquire(
            Guid.NewGuid(), data.Project.Id, newerWorkspace.Id, 2, new byte[16], Now));
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new RecordCheckpointReviewCommandHandler(
            dbContext, new CountingEvidenceReader(data.Checkpoint.FingerprintSha256), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new RecordCheckpointReviewCommand(
            data.Project.Id, data.Checkpoint.Id, data.Execution.Id, ReviewActorKind.Human, ReviewDecision.Approved), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("reviews.checkpoint_not_current", result.Errors[0].Code);
        Assert.Empty(dbContext.CheckpointReviews);
    }

    [Fact]
    public async Task Inactive_lease_is_rejected_without_mutation()
    {
        await using var dbContext = fixture.CreateContext();
        var data = await AddReadyEvidenceAsync(dbContext, VerificationExecutionStatus.Passed, VerificationExecutionOutcome.Exited, 0, addActiveLease: false);
        var handler = new RecordCheckpointReviewCommandHandler(
            dbContext, new CountingEvidenceReader(data.Checkpoint.FingerprintSha256), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new RecordCheckpointReviewCommand(
            data.Project.Id, data.Checkpoint.Id, data.Execution.Id, ReviewActorKind.Human, ReviewDecision.Approved), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("reviews.workspace_not_ready", result.Errors[0].Code);
        Assert.Empty(dbContext.CheckpointReviews);
    }

    [Fact]
    public async Task Running_evidence_is_rejected_without_mutation()
    {
        await using var dbContext = fixture.CreateContext();
        var data = await AddReadyEvidenceAsync(dbContext, status: null, outcome: null, exitCode: null);
        var handler = new RecordCheckpointReviewCommandHandler(
            dbContext, new FixedEvidenceReader(data.Checkpoint.FingerprintSha256), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new RecordCheckpointReviewCommand(
            data.Project.Id, data.Checkpoint.Id, data.Execution.Id, ReviewActorKind.Human, ReviewDecision.ChangesRequested), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("reviews.evidence_not_terminal", result.Errors[0].Code);
        Assert.Empty(dbContext.CheckpointReviews);
    }

    [Fact]
    public async Task Review_projection_is_bounded_before_materialization()
    {
        await using (var dbContext = fixture.CreateContext())
        {
            var data = await AddReadyEvidenceAsync(dbContext, VerificationExecutionStatus.Passed, VerificationExecutionOutcome.Exited, 0);
            for (var index = 0; index < 51; index++)
            {
                var reviewId = Guid.NewGuid();
                var member = CheckpointReviewEvidence.Observe(
                    Guid.NewGuid(), reviewId, data.Execution.VerificationCommandId, data.Execution.Id, data.Execution.ExecutionNumber,
                    data.Execution.CheckpointFingerprintSha256, data.Execution.Status, data.Execution.Outcome, data.Execution.ExitCode);
                dbContext.CheckpointReviews.Add(CheckpointReview.Record(
                    reviewId, data.Project.Id, data.Workspace.Id, data.Checkpoint.Id, data.Checkpoint.CheckpointNumber,
                    data.Checkpoint.FingerprintSha256, ReviewActorKind.Human, ReviewDecision.ChangesRequested, Now.AddSeconds(index), [member]));
            }

            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        await using var queryContext = fixture.CreateContext();
        var projected = await new GetProjectCheckpointReviewsQueryHandler(
            queryContext, new FixedEvidenceReader(new string('a', 64))).HandleAsync(
                new GetProjectCheckpointReviewsQuery(queryContext.Projects.Select(project => project.Id).First()), CancellationToken.None);

        Assert.True(projected.IsSuccess);
        Assert.Equal(50, projected.Value.Count);
        Assert.Equal(
            Enumerable.Range(1, 50).Reverse().Select(index => Now.AddSeconds(index)),
            projected.Value.Select(review => review.RecordedAtUtc));
        Assert.Empty(queryContext.ChangeTracker.Entries<CheckpointReview>());
    }

    [Fact]
    public async Task Stale_approved_review_remains_in_history_but_is_excluded_from_applicable_projection()
    {
        await using var dbContext = fixture.CreateContext();
        var data = await AddReadyEvidenceAsync(dbContext, VerificationExecutionStatus.Passed, VerificationExecutionOutcome.Exited, 0);
        var recordHandler = new RecordCheckpointReviewCommandHandler(
            dbContext, new FixedEvidenceReader(data.Checkpoint.FingerprintSha256), new FixedTimeProvider(Now));
        var recorded = await recordHandler.HandleAsync(new RecordCheckpointReviewCommand(
            data.Project.Id, data.Checkpoint.Id, data.Execution.Id, ReviewActorKind.Human, ReviewDecision.Approved), CancellationToken.None);
        Assert.True(recorded.IsSuccess);

        var queryHandler = new GetProjectCheckpointReviewsQueryHandler(
            dbContext, new FixedEvidenceReader(new string('c', 64)));
        var projected = await queryHandler.HandleAsync(
            new GetProjectCheckpointReviewsQuery(data.Project.Id), CancellationToken.None);

        var review = Assert.Single(projected.Value);
        Assert.False(review.IsApplicable);
        Assert.Equal(CheckpointReviewApplicabilityReasonCodes.SourceChanged, review.StaleReasonCode);
        Assert.Single(dbContext.CheckpointReviews);
        Assert.Equal(ReviewDecision.Approved, dbContext.CheckpointReviews.Single().Decision);
    }

    private async Task<(Project Project, GitWorkspace Workspace, GitCheckpoint Checkpoint, VerificationExecution Execution)> AddReadyEvidenceAsync(
        DevalCopilotDbContext dbContext, VerificationExecutionStatus? status, VerificationExecutionOutcome? outcome, int? exitCode, bool addActiveLease = true)
    {
        var project = Project.Register(Guid.NewGuid(), "Review project", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var workspace = GitWorkspace.Prepare(Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        workspace.MarkReady();
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), Hash, []);
        var command = VerificationCommand.Configure(Guid.NewGuid(), project.Id, 1, "Tests", @"C:\dotnet.exe", ["test"], 60, true, Now);
        var execution = VerificationExecution.Claim(Guid.NewGuid(), project.Id, 1, workspace, checkpoint, command, Now);
        if (status == VerificationExecutionStatus.Interrupted)
        {
            execution.MarkDispatched(Now);
            execution.Interrupt(Now);
        }
        else if (status == VerificationExecutionStatus.SourceChanged && !outcome.HasValue)
        {
            execution.MarkSourceChangedBeforeDispatch(new string('c', 64), Now);
        }
        else if (status.HasValue)
        {
            execution.MarkDispatched(Now);
            execution.Complete(outcome!.Value, exitCode, status == VerificationExecutionStatus.SourceChanged ? new string('c', 64) : checkpoint.FingerprintSha256, Now);
        }
        dbContext.Projects.Add(project);
        dbContext.GitWorkspaces.Add(workspace);
        dbContext.GitCheckpoints.Add(checkpoint);
        dbContext.VerificationCommands.Add(command);
        if (addActiveLease)
        {
            dbContext.RepositoryMutationLeases.Add(RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, Guid.NewGuid().ToByteArray(), Now));
        }
        dbContext.VerificationExecutions.Add(execution);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        return (project, workspace, checkpoint, execution);
    }

    private sealed class FixedEvidenceReader(string fingerprint) : IGitWorkspaceEvidenceReader
    {
        public Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken) =>
            Task.FromResult(new GitWorkspaceEvidenceResult(GitWorkspaceEvidenceOutcome.Success, new string('a', 40), fingerprint, [], null));
    }

    private sealed class CountingEvidenceReader(string fingerprint) : IGitWorkspaceEvidenceReader
    {
        public int CaptureCount { get; private set; }

        public Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken)
        {
            CaptureCount++;
            return Task.FromResult(new GitWorkspaceEvidenceResult(GitWorkspaceEvidenceOutcome.Success, new string('a', 40), fingerprint, [], null));
        }
    }

    private const string Hash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
}
