using System.Reflection;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Commands.CreateChallengeResolutionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Application.Features.Runs.Commands.ReconcileInterruptedAgentAttempts;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>
/// The manual Resolver format repair through the real supervisor and the process-double adapter: an invalid
/// source, one manual fresh repair request, the unchanged dispatch of that repair from its sealed manifest after a
/// restart, and an ordinary validated result. Only the provider adapter and the Git evidence reader are doubles.
/// </summary>
public sealed partial class ChallengeResolutionSupervisorHostedTests
{
    private const string InvalidResolutionResponseJson = "{ this is not valid resolution json";

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
    public async Task A_manual_repair_of_an_invalid_resolution_is_replayed_after_restart_from_its_sealed_manifest_and_yields_an_ordinary_resolution()
    {
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(_ => SequencedGitWorkspaceEvidenceReader.Matching(Fingerprint));
        var invalidAdapter = new FakeChallengeResolutionAdapter(_artifactStore) { FinalResponseJsonToWrite = InvalidResolutionResponseJson };
        Guid runId;
        Guid sourceId;
        Guid repairId;
        Guid originalProposalId;
        List<Guid> challengeIds;

        await using (var first = BuildServiceProvider(evidenceReader, invalidAdapter))
        {
            (runId, sourceId, _, originalProposalId, challengeIds) = await SeedEligibleChallengeResolutionAttemptAsync(first, evidenceReader);
            Assert.Equal(AttemptStatus.Failed, await RunRepairSupervisorUntilTerminalAsync(first, sourceId));
            Assert.Equal(1, invalidAdapter.InvocationCount);

            await using var scope = first.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            Assert.Equal(AgentOutcome.InvalidStructuredOutput, (await dbContext.Attempts.FindAsync(sourceId))!.AgentOutcome);
            Assert.Empty(dbContext.CollaborationMessages.Where(m => m.AttemptId == sourceId));

            var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
            var refused = await mediator.SendAsync(
                CreateChallengeResolutionAttemptCommand.ForRepair(runId, Guid.NewGuid()), CancellationToken.None);
            Assert.Equal("agent_attempts.repair_source_not_found", Assert.Single(refused.Errors).Code);
            Assert.Equal(1, invalidAdapter.InvocationCount);

            var claim = await mediator.SendAsync(CreateChallengeResolutionAttemptCommand.ForRepair(runId, sourceId), CancellationToken.None);
            Assert.True(claim.IsSuccess);
            repairId = claim.Value.AttemptId;
            Assert.Equal(1, invalidAdapter.InvocationCount);
        }

        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_databasePath}"));

        var validAdapter = new FakeChallengeResolutionAdapter(_artifactStore) { FinalResponseJsonToWrite = ResolvedFinalResponseJson(challengeIds) };
        await using var restarted = BuildServiceProvider(evidenceReader, validAdapter);
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
        Assert.Contains(originalProposalId.ToString(), manifest.Text, StringComparison.OrdinalIgnoreCase);
        Assert.All(challengeIds, id => Assert.Contains(id.ToString(), manifest.Text, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(sourceId.ToString(), manifest.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(InvalidResolutionResponseJson, manifest.Text, StringComparison.Ordinal);

        Assert.DoesNotContain(
            typeof(ChallengeResolutionInvocationRequest).GetProperties(BindingFlags.Public | BindingFlags.Instance),
            property => property.Name.Contains("Repair", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Source", StringComparison.OrdinalIgnoreCase));

        await using var scope2 = restarted.CreateAsyncScope();
        var db = scope2.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var repair = await db.Attempts.AsNoTracking().SingleAsync(a => a.Id == repairId);
        Assert.Equal(AgentOutcome.Resolved, repair.AgentOutcome);
        Assert.Equal(sourceId, repair.AgentRepairSourceAttemptId);
        var messages = db.CollaborationMessages.Where(m => m.AttemptId == repairId).ToList();
        Assert.Equal(challengeIds.Count, messages.Count(m => m.Type == CollaborationMessageType.Decision));
        Assert.Equal(originalProposalId, Assert.Single(messages, m => m.Type == CollaborationMessageType.Proposal).InReplyToMessageId);
        Assert.Empty(db.CollaborationMessages.Where(m => m.RunId == runId && m.Type == CollaborationMessageType.Escalation));

        var second = await scope2.ServiceProvider.GetRequiredService<IApplicationMediator>()
            .SendAsync(CreateChallengeResolutionAttemptCommand.ForRepair(runId, sourceId), CancellationToken.None);
        Assert.Equal("agent_attempts.repair_already_requested", Assert.Single(second.Errors).Code);
    }

    [Fact]
    public async Task An_invalid_repair_records_no_semantic_result_and_cannot_be_repaired_again()
    {
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(_ => SequencedGitWorkspaceEvidenceReader.Matching(Fingerprint));
        var adapter = new FakeChallengeResolutionAdapter(_artifactStore) { FinalResponseJsonToWrite = InvalidResolutionResponseJson };
        await using var provider = BuildServiceProvider(evidenceReader, adapter);
        var (runId, sourceId, _, _, _) = await SeedEligibleChallengeResolutionAttemptAsync(provider, evidenceReader);
        Assert.Equal(AttemptStatus.Failed, await RunRepairSupervisorUntilTerminalAsync(provider, sourceId));

        Guid repairId;
        await using (var scope = provider.CreateAsyncScope())
        {
            var claim = await scope.ServiceProvider.GetRequiredService<IApplicationMediator>()
                .SendAsync(CreateChallengeResolutionAttemptCommand.ForRepair(runId, sourceId), CancellationToken.None);
            Assert.True(claim.IsSuccess);
            repairId = claim.Value.AttemptId;
        }

        Assert.Equal(AttemptStatus.Failed, await RunRepairSupervisorUntilTerminalAsync(provider, repairId));
        Assert.Equal(2, adapter.InvocationCount);

        await using var verifyScope = provider.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        Assert.Equal(AgentOutcome.InvalidStructuredOutput, (await db.Attempts.AsNoTracking().SingleAsync(a => a.Id == repairId)).AgentOutcome);
        Assert.Empty(db.CollaborationMessages.Where(m => m.AttemptId == repairId || m.AttemptId == sourceId));
        var mediator = verifyScope.ServiceProvider.GetRequiredService<IApplicationMediator>();
        var ofRepair = await mediator.SendAsync(CreateChallengeResolutionAttemptCommand.ForRepair(runId, repairId), CancellationToken.None);
        Assert.Equal("agent_attempts.repair_of_repair_forbidden", Assert.Single(ofRepair.Errors).Code);
        var ofSource = await mediator.SendAsync(CreateChallengeResolutionAttemptCommand.ForRepair(runId, sourceId), CancellationToken.None);
        Assert.Equal("agent_attempts.repair_already_requested", Assert.Single(ofSource.Errors).Code);
        Assert.Equal(2, adapter.InvocationCount);
    }

    [Fact]
    public async Task A_dispatched_repair_interrupted_by_a_restart_is_never_reinvoked_and_no_further_claim_is_possible()
    {
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(_ => SequencedGitWorkspaceEvidenceReader.Matching(Fingerprint));
        Guid runId;
        Guid sourceId;
        Guid repairId;
        List<Guid> challengeIds;

        await using (var first = BuildServiceProvider(
            evidenceReader, new FakeChallengeResolutionAdapter(_artifactStore) { FinalResponseJsonToWrite = InvalidResolutionResponseJson }))
        {
            (runId, sourceId, _, _, challengeIds) = await SeedEligibleChallengeResolutionAttemptAsync(first, evidenceReader);
            Assert.Equal(AttemptStatus.Failed, await RunRepairSupervisorUntilTerminalAsync(first, sourceId));

            await using var scope = first.CreateAsyncScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
            var claim = await mediator.SendAsync(CreateChallengeResolutionAttemptCommand.ForRepair(runId, sourceId), CancellationToken.None);
            Assert.True(claim.IsSuccess);
            repairId = claim.Value.AttemptId;
            Assert.True((await mediator.SendAsync(new MarkAgentAttemptDispatchedCommand(runId, repairId), CancellationToken.None)).IsSuccess);
        }

        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_databasePath}"));

        var restartedAdapter = new FakeChallengeResolutionAdapter(_artifactStore) { FinalResponseJsonToWrite = ResolvedFinalResponseJson(challengeIds) };
        await using var restarted = BuildServiceProvider(evidenceReader, restartedAdapter);
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
            .SendAsync(CreateChallengeResolutionAttemptCommand.ForRepair(runId, sourceId), CancellationToken.None);
        Assert.True(again.IsFailure);
        Assert.Equal(1, await db.Attempts.CountAsync(a => a.AgentRepairSourceAttemptId == sourceId));
    }
}
