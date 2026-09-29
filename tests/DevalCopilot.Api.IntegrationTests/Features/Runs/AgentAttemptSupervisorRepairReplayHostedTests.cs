using System.Reflection;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodexPlanningAttempt;
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
/// Restart replay of the one manual format-repair attempt through the real supervisor: a repair is
/// an ordinary read-only Planner attempt, so the unchanged dispatch, single-invocation guard,
/// interruption reconciliation, parser, and one-Proposal ledger rule must all apply to it. Only
/// the provider adapter and Git evidence reader are doubles; no real provider is called.
/// </summary>
public sealed partial class AgentAttemptSupervisorHostedTests
{
    private const string InvalidFinalResponseJson = "{ this is not valid proposal json";

    /// <summary>Records exactly what the supervisor passes to the provider boundary.</summary>
    private sealed class RecordingCodexPlanningAdapter(IArtifactStore artifactStore, string finalResponseJson) : ICodexPlanningAdapter
    {
        private readonly List<CodexPlanningInvocationRequest> _requests = [];

        public IReadOnlyList<CodexPlanningInvocationRequest> Requests
        {
            get
            {
                lock (_requests)
                {
                    return _requests.ToArray();
                }
            }
        }

        public async Task<CodexPlanningInvocationResult> InvokeAsync(CodexPlanningInvocationRequest request, CancellationToken cancellationToken)
        {
            lock (_requests)
            {
                _requests.Add(request);
            }

            var path = artifactStore.GetPartialPath(request.RunId, request.AttemptId, ArtifactPurpose.AgentFinalResponse);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, finalResponseJson, cancellationToken);

            return new CodexPlanningInvocationResult(
                CodexPlanningInvocationOutcome.Exited, false, false, null, ProcessEvidence: TestProcessEvidence.ReportedCleanExit);
        }
    }

    [Fact]
    public async Task A_claimed_repair_is_replayed_after_restart_through_the_unchanged_read_only_dispatch_and_yields_one_proposal()
    {
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(_ => SequencedGitWorkspaceEvidenceReader.Matching(Fingerprint));
        var invalidAdapter = new RecordingCodexPlanningAdapter(_artifactStore, InvalidFinalResponseJson);
        Guid runId;
        Guid sourceId;
        Guid repairId;

        await using (var firstProvider = BuildServiceProvider(evidenceReader, invalidAdapter))
        {
            (runId, sourceId, _, _) = await SeedEligibleAgentAttemptAsync(firstProvider);
            var source = await RunUntilTerminalAsync(firstProvider, sourceId);
            Assert.Equal(AgentOutcome.InvalidStructuredOutput, source.AgentOutcome);

            // The invalid response appended nothing to the ledger.
            await using var scope = firstProvider.CreateAsyncScope();
            Assert.Empty(scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>().CollaborationMessages.Where(m => m.RunId == runId));

            var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
            var claim = await mediator.SendAsync(new CreateCodexPlanningAttemptCommand(runId, sourceId), CancellationToken.None);
            Assert.True(claim.IsSuccess);
            repairId = claim.Value.AttemptId;
        }

        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_databasePath}"));

        // A process restart: a fresh provider and adapter; the claimed-but-undispatched repair is
        // still Running in the database and is picked up by the ordinary supervisor.
        var validAdapter = new RecordingCodexPlanningAdapter(_artifactStore, ValidFinalResponseJson);
        await using var restartedProvider = BuildServiceProvider(evidenceReader, validAdapter);
        var repair = await RunUntilTerminalAsync(restartedProvider, repairId);

        Assert.Equal(AttemptStatus.Completed, repair.Status);
        Assert.Equal(AgentOutcome.Proposed, repair.AgentOutcome);
        Assert.Equal(sourceId, repair.AgentRepairSourceAttemptId);
        Assert.Single(invalidAdapter.Requests);
        var request = Assert.Single(validAdapter.Requests);
        Assert.Equal(repairId, request.AttemptId);
        Assert.Equal(TimeSpan.FromMinutes(10), request.Timeout);
        Assert.Null(request.RequestedModel);
        Assert.Null(request.RequestedEffort);

        // The provider received the repair's own sealed manifest: the fixed reminder and nothing from the source.
        var manifest = await _artifactStore.VerifyAndReadSealedAsync(
            request.ContextManifestRelativeStoragePath, request.ContextManifestByteLength, request.ContextManifestContentHash,
            0, 32 * 1024, CancellationToken.None);
        Assert.Equal(SealedReadStatus.Ok, manifest.Status);
        Assert.Contains("formatRepairNotice", manifest.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(sourceId.ToString(), manifest.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(InvalidFinalResponseJson, manifest.Text, StringComparison.Ordinal);

        // The dispatch contract has no repair-specific input, so the read-only adapter arguments are unchanged.
        Assert.DoesNotContain(
            typeof(CodexPlanningInvocationRequest).GetProperties(BindingFlags.Public | BindingFlags.Instance),
            property => property.Name.Contains("Repair", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Source", StringComparison.OrdinalIgnoreCase));

        await using var scope2 = restartedProvider.CreateAsyncScope();
        var dbContext = scope2.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var message = Assert.Single(dbContext.CollaborationMessages.Where(m => m.RunId == runId));
        Assert.Equal(repairId, message.AttemptId);
        Assert.Equal(CollaborationMessageType.Proposal, message.Type);

        // The single repair is consumed: a second one is refused even after a completed repair.
        var second = await scope2.ServiceProvider.GetRequiredService<IApplicationMediator>()
            .SendAsync(new CreateCodexPlanningAttemptCommand(runId, sourceId), CancellationToken.None);
        Assert.True(second.IsFailure);
        Assert.Equal("agent_attempts.repair_already_requested", Assert.Single(second.Errors).Code);
    }

    [Fact]
    public async Task A_dispatched_repair_interrupted_by_a_restart_is_never_reinvoked_and_no_further_claim_is_possible()
    {
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(_ => SequencedGitWorkspaceEvidenceReader.Matching(Fingerprint));
        Guid runId;
        Guid sourceId;
        Guid repairId;

        await using (var firstProvider = BuildServiceProvider(evidenceReader, new RecordingCodexPlanningAdapter(_artifactStore, InvalidFinalResponseJson)))
        {
            (runId, sourceId, _, _) = await SeedEligibleAgentAttemptAsync(firstProvider);
            await RunUntilTerminalAsync(firstProvider, sourceId);

            await using var scope = firstProvider.CreateAsyncScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
            var claim = await mediator.SendAsync(new CreateCodexPlanningAttemptCommand(runId, sourceId), CancellationToken.None);
            Assert.True(claim.IsSuccess);
            repairId = claim.Value.AttemptId;

            // The host's durable execution-start claim, then a crash before any result is recorded.
            var dispatch = await mediator.SendAsync(new MarkAgentAttemptDispatchedCommand(runId, repairId), CancellationToken.None);
            Assert.True(dispatch.IsSuccess);
        }

        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_databasePath}"));

        var restartedAdapter = new RecordingCodexPlanningAdapter(_artifactStore, ValidFinalResponseJson);
        await using var restartedProvider = BuildServiceProvider(evidenceReader, restartedAdapter);
        await using (var reconcileScope = restartedProvider.CreateAsyncScope())
        {
            var reconcile = await reconcileScope.ServiceProvider.GetRequiredService<IApplicationMediator>()
                .SendAsync(new ReconcileInterruptedAgentAttemptsCommand(), CancellationToken.None);
            Assert.True(reconcile.IsSuccess);
            Assert.Equal(1, reconcile.Value);
        }

        var supervisor = CreateSupervisor(restartedProvider);
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

        Assert.Empty(restartedAdapter.Requests);

        await using var scope2 = restartedProvider.CreateAsyncScope();
        var dbContext = scope2.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var repair = await dbContext.Attempts.AsNoTracking().SingleAsync(a => a.Id == repairId);
        Assert.Equal(AttemptStatus.Interrupted, repair.Status);
        Assert.Equal(sourceId, repair.AgentRepairSourceAttemptId);
        Assert.Empty(dbContext.CollaborationMessages.Where(m => m.RunId == runId));

        // Reconciling an interrupted Agent attempt ends the run, so the ordinary run gate refuses any
        // further claim; the repair link stays durable and the source is never repaired twice.
        var again = await scope2.ServiceProvider.GetRequiredService<IApplicationMediator>()
            .SendAsync(new CreateCodexPlanningAttemptCommand(runId, sourceId), CancellationToken.None);
        Assert.True(again.IsFailure);
        Assert.Equal("runs.not_active", Assert.Single(again.Errors).Code);
        Assert.Equal(1, await dbContext.Attempts.CountAsync(a => a.AgentRepairSourceAttemptId == sourceId));
    }
}
