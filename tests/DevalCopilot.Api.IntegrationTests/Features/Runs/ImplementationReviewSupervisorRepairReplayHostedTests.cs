using System.Reflection;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodeReviewAttempt;
using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Application.Features.Runs.Commands.ReconcileInterruptedAgentAttempts;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>
/// The manual CodeReviewer format repair through the real supervisor and the process-double adapter: an invalid
/// source, one manual fresh repair request that retains the exact ExecutionReport and verification set, the
/// unchanged dispatch of that repair from its sealed manifest after a restart, and an ordinary validated result.
/// Only the provider adapter and the Git evidence reader are doubles.
/// </summary>
public sealed partial class ImplementationReviewSupervisorHostedTests
{
    private const string InvalidReviewResponseJson = "{ this is not valid review json";

    /// <summary>Calls 1-3 are the seeding-time claims against the starting checkpoint; everything after (the
    /// supervisor's captures and any repair claim) is against the result checkpoint the review is bound to.</summary>
    private static SequencedGitWorkspaceEvidenceReader ReviewEvidenceReader() => new(
        call => call <= 3
            ? SequencedGitWorkspaceEvidenceReader.Matching(Fingerprint)
            : SequencedGitWorkspaceEvidenceReader.Matching(ResultFingerprint));

    private async Task<AttemptStatus> RunRepairSupervisorUntilTerminalAsync(ServiceProvider provider, Guid attemptId)
    {
        var supervisor = CreateSupervisor(provider);
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            return await PollForTerminalStatusAsync(provider, attemptId);
        }
        finally
        {
            using var stopCancellation = new CancellationTokenSource(PollTimeout);
            await supervisor.StopAsync(stopCancellation.Token);
        }
    }

    [Fact]
    public async Task A_manual_repair_of_an_invalid_review_is_replayed_after_restart_from_its_sealed_manifest_and_yields_an_ordinary_approval()
    {
        var evidenceReader = ReviewEvidenceReader();
        var invalidAdapter = new FakeImplementationReviewAdapter(_artifactStore) { FinalResponseJsonToWrite = InvalidReviewResponseJson };
        Guid runId;
        Guid sourceId;
        Guid repairId;
        Guid executionId;
        Guid executionReportId;
        Guid resultCheckpointId;

        await using (var first = BuildServiceProvider(evidenceReader, invalidAdapter))
        {
            (runId, sourceId, _, resultCheckpointId, executionId, _, executionReportId) =
                await SeedEligibleCodeReviewAttemptAsync(first, evidenceReader);
            Assert.Equal(AttemptStatus.Failed, await RunRepairSupervisorUntilTerminalAsync(first, sourceId));
            Assert.Equal(1, invalidAdapter.InvocationCount);

            await using var scope = first.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            Assert.Equal(AgentOutcome.InvalidStructuredOutput, (await dbContext.Attempts.FindAsync(sourceId))!.AgentOutcome);
            Assert.Empty(dbContext.CollaborationMessages.Where(m => m.AttemptId == sourceId));
            Assert.Empty(dbContext.CheckpointReviews.Where(r => r.GitCheckpointId == resultCheckpointId));

            var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
            var refused = await mediator.SendAsync(CreateCodeReviewAttemptCommand.ForRepair(runId, Guid.NewGuid()), CancellationToken.None);
            Assert.Equal("agent_attempts.repair_source_not_found", Assert.Single(refused.Errors).Code);
            Assert.Equal(1, invalidAdapter.InvocationCount);

            var claim = await mediator.SendAsync(CreateCodeReviewAttemptCommand.ForRepair(runId, sourceId), CancellationToken.None);
            Assert.True(claim.IsSuccess, claim.IsFailure ? claim.Errors[0].Code : null);
            repairId = claim.Value.AttemptId;
            Assert.Equal(1, invalidAdapter.InvocationCount);
        }

        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_databasePath}"));

        var validAdapter = new FakeImplementationReviewAdapter(_artifactStore) { FinalResponseJsonToWrite = ApprovedFinalResponseJson() };
        await using var restarted = BuildServiceProvider(
            new SequencedGitWorkspaceEvidenceReader(_ => SequencedGitWorkspaceEvidenceReader.Matching(ResultFingerprint)), validAdapter);
        Assert.Equal(AttemptStatus.Completed, await RunRepairSupervisorUntilTerminalAsync(restarted, repairId));

        Assert.Equal(1, invalidAdapter.InvocationCount);
        Assert.Equal(1, validAdapter.InvocationCount);
        var request = validAdapter.LastRequest!;
        Assert.Equal(repairId, request.AttemptId);
        Assert.Equal(TimeSpan.FromMinutes(10), request.Timeout);
        Assert.Null(request.RequestedModel);
        Assert.Null(request.RequestedEffort);

        var manifest = await _artifactStore.VerifyAndReadSealedAsync(
            request.ContextManifestRelativeStoragePath, request.ContextManifestByteLength, request.ContextManifestContentHash,
            0, 32 * 1024, CancellationToken.None);
        Assert.Equal(SealedReadStatus.Ok, manifest.Status);
        Assert.Contains("formatRepairNotice", manifest.Text, StringComparison.Ordinal);
        Assert.Contains(executionReportId.ToString(), manifest.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(sourceId.ToString(), manifest.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(InvalidReviewResponseJson, manifest.Text, StringComparison.Ordinal);

        Assert.DoesNotContain(
            typeof(ImplementationReviewInvocationRequest).GetProperties(BindingFlags.Public | BindingFlags.Instance),
            property => property.Name.Contains("Repair", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Source", StringComparison.OrdinalIgnoreCase));

        await using var scope2 = restarted.CreateAsyncScope();
        var db = scope2.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var repair = await db.Attempts.AsNoTracking().SingleAsync(a => a.Id == repairId);
        Assert.Equal(AgentOutcome.ReviewApproved, repair.AgentOutcome);
        Assert.Equal(sourceId, repair.AgentRepairSourceAttemptId);
        var approval = Assert.Single(db.CollaborationMessages.Where(m => m.AttemptId == repairId));
        Assert.Equal(CollaborationMessageType.ReviewApproval, approval.Type);
        Assert.Equal(executionReportId, approval.InReplyToMessageId);
        var review = Assert.Single(db.CheckpointReviews.Include(r => r.Evidence).Where(r => r.GitCheckpointId == resultCheckpointId));
        Assert.Equal(ReviewDecision.Approved, review.Decision);
        Assert.Equal(executionId, Assert.Single(review.Evidence).VerificationExecutionId);
        Assert.Empty(db.CollaborationMessages.Where(m => m.AttemptId == sourceId));

        var second = await scope2.ServiceProvider.GetRequiredService<IApplicationMediator>()
            .SendAsync(CreateCodeReviewAttemptCommand.ForRepair(runId, sourceId), CancellationToken.None);
        Assert.Equal("agent_attempts.repair_already_requested", Assert.Single(second.Errors).Code);
    }

    [Fact]
    public async Task An_invalid_repair_records_no_semantic_result_and_cannot_be_repaired_again()
    {
        var evidenceReader = ReviewEvidenceReader();
        var adapter = new FakeImplementationReviewAdapter(_artifactStore) { FinalResponseJsonToWrite = InvalidReviewResponseJson };
        await using var provider = BuildServiceProvider(evidenceReader, adapter);
        var (runId, sourceId, _, resultCheckpointId, _, _, _) = await SeedEligibleCodeReviewAttemptAsync(provider, evidenceReader);
        Assert.Equal(AttemptStatus.Failed, await RunRepairSupervisorUntilTerminalAsync(provider, sourceId));

        Guid repairId;
        await using (var scope = provider.CreateAsyncScope())
        {
            var claim = await scope.ServiceProvider.GetRequiredService<IApplicationMediator>()
                .SendAsync(CreateCodeReviewAttemptCommand.ForRepair(runId, sourceId), CancellationToken.None);
            Assert.True(claim.IsSuccess, claim.IsFailure ? claim.Errors[0].Code : null);
            repairId = claim.Value.AttemptId;
        }

        Assert.Equal(AttemptStatus.Failed, await RunRepairSupervisorUntilTerminalAsync(provider, repairId));
        Assert.Equal(2, adapter.InvocationCount);

        await using var verifyScope = provider.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        Assert.Equal(AgentOutcome.InvalidStructuredOutput, (await db.Attempts.AsNoTracking().SingleAsync(a => a.Id == repairId)).AgentOutcome);
        Assert.Empty(db.CollaborationMessages.Where(m => m.AttemptId == repairId || m.AttemptId == sourceId));
        Assert.Empty(db.CheckpointReviews.Where(r => r.GitCheckpointId == resultCheckpointId));
        var mediator = verifyScope.ServiceProvider.GetRequiredService<IApplicationMediator>();
        var ofRepair = await mediator.SendAsync(CreateCodeReviewAttemptCommand.ForRepair(runId, repairId), CancellationToken.None);
        Assert.Equal("agent_attempts.repair_of_repair_forbidden", Assert.Single(ofRepair.Errors).Code);
        var ofSource = await mediator.SendAsync(CreateCodeReviewAttemptCommand.ForRepair(runId, sourceId), CancellationToken.None);
        Assert.Equal("agent_attempts.repair_already_requested", Assert.Single(ofSource.Errors).Code);
        Assert.Equal(2, adapter.InvocationCount);
    }

    [Fact]
    public async Task A_dispatched_repair_interrupted_by_a_restart_is_never_reinvoked_and_no_further_claim_is_possible()
    {
        var evidenceReader = ReviewEvidenceReader();
        Guid runId;
        Guid sourceId;
        Guid repairId;

        await using (var first = BuildServiceProvider(
            evidenceReader, new FakeImplementationReviewAdapter(_artifactStore) { FinalResponseJsonToWrite = InvalidReviewResponseJson }))
        {
            (runId, sourceId, _, _, _, _, _) = await SeedEligibleCodeReviewAttemptAsync(first, evidenceReader);
            Assert.Equal(AttemptStatus.Failed, await RunRepairSupervisorUntilTerminalAsync(first, sourceId));

            await using var scope = first.CreateAsyncScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
            var claim = await mediator.SendAsync(CreateCodeReviewAttemptCommand.ForRepair(runId, sourceId), CancellationToken.None);
            Assert.True(claim.IsSuccess, claim.IsFailure ? claim.Errors[0].Code : null);
            repairId = claim.Value.AttemptId;
            Assert.True((await mediator.SendAsync(new MarkAgentAttemptDispatchedCommand(runId, repairId), CancellationToken.None)).IsSuccess);
        }

        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_databasePath}"));

        var restartedAdapter = new FakeImplementationReviewAdapter(_artifactStore) { FinalResponseJsonToWrite = ApprovedFinalResponseJson() };
        await using var restarted = BuildServiceProvider(
            new SequencedGitWorkspaceEvidenceReader(_ => SequencedGitWorkspaceEvidenceReader.Matching(ResultFingerprint)), restartedAdapter);
        await using (var reconcileScope = restarted.CreateAsyncScope())
        {
            var reconcile = await reconcileScope.ServiceProvider.GetRequiredService<IApplicationMediator>()
                .SendAsync(new ReconcileInterruptedAgentAttemptsCommand(), CancellationToken.None);
            Assert.True(reconcile.IsSuccess);
            Assert.Equal(1, reconcile.Value);
        }

        var supervisor = CreateSupervisor(restarted);
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(1200));
        }
        finally
        {
            using var stopCancellation = new CancellationTokenSource(PollTimeout);
            await supervisor.StopAsync(stopCancellation.Token);
        }

        Assert.Equal(0, restartedAdapter.InvocationCount);

        await using var scope2 = restarted.CreateAsyncScope();
        var db = scope2.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var repair = await db.Attempts.AsNoTracking().SingleAsync(a => a.Id == repairId);
        Assert.Equal(AttemptStatus.Interrupted, repair.Status);
        Assert.Equal(sourceId, repair.AgentRepairSourceAttemptId);
        Assert.Empty(db.CollaborationMessages.Where(m => m.AttemptId == repairId || m.AttemptId == sourceId));
        var again = await scope2.ServiceProvider.GetRequiredService<IApplicationMediator>()
            .SendAsync(CreateCodeReviewAttemptCommand.ForRepair(runId, sourceId), CancellationToken.None);
        Assert.True(again.IsFailure);
        Assert.Equal(1, await db.Attempts.CountAsync(a => a.AgentRepairSourceAttemptId == sourceId));
    }
}
