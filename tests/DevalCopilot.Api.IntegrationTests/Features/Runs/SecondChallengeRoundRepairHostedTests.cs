using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Commands.CreateChallengeResolutionAttempt;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>
/// The manual format repair of the second, final challenge-resolution round: a structurally invalid second
/// resolution is repaired once from its sealed manifest after a restart, and only the repair's valid result records
/// the depth-two revised Proposal and exactly one bounded escalation — the invalid source recorded nothing, and the
/// repair adds no challenge round.
/// </summary>
public sealed partial class SecondChallengeRoundSupervisorHostedTests
{
    private const string InvalidSecondResolutionJson = "{ this is not valid resolution json";

    [Fact]
    public async Task A_repaired_invalid_second_resolution_records_the_depth_two_proposal_and_exactly_one_escalation()
    {
        var evidence = new MatchingEvidenceReader();
        var invalidAdapter = new FakeAdapter(_artifactStore) { FinalResponseJsonToWrite = InvalidSecondResolutionJson };
        Seeded seeded;
        Guid repairId;

        await using (var first = BuildServiceProvider(evidence, invalidAdapter))
        {
            seeded = await SeedClaimedSecondResolutionAsync(first);
            await RunSupervisorAsync(first, async () =>
                Assert.Equal(AttemptStatus.Failed, await PollForTerminalStatusAsync(first, seeded.SecondResolverAttemptId)));
            Assert.Equal(1, invalidAdapter.InvocationCount);

            await using var scope = first.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            Assert.Equal(AgentOutcome.InvalidStructuredOutput, (await db.Attempts.FindAsync(seeded.SecondResolverAttemptId))!.AgentOutcome);
            Assert.Empty(db.CollaborationMessages.Where(m => m.AttemptId == seeded.SecondResolverAttemptId));
            Assert.Empty(db.CollaborationMessages.Where(m => m.RunId == seeded.RunId && m.Type == CollaborationMessageType.Escalation));

            var claim = await scope.ServiceProvider.GetRequiredService<IApplicationMediator>().SendAsync(
                CreateChallengeResolutionAttemptCommand.ForRepair(seeded.RunId, seeded.SecondResolverAttemptId), CancellationToken.None);
            Assert.True(claim.IsSuccess);
            repairId = claim.Value.AttemptId;
        }

        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_databasePath}"));

        var validAdapter = new FakeAdapter(_artifactStore)
        {
            FinalResponseJsonToWrite = ResolvedResponseJson(seeded.SecondChallengeIds, "Second revised scope"),
        };
        await using var restarted = BuildServiceProvider(evidence, validAdapter);
        await RunSupervisorAsync(restarted, async () =>
            Assert.Equal(AttemptStatus.Completed, await PollForTerminalStatusAsync(restarted, repairId)));

        Assert.Equal(1, invalidAdapter.InvocationCount);
        Assert.Equal(1, validAdapter.InvocationCount);
        await AssertResolvedWithOneEscalationAsync(restarted, validAdapter, seeded, repairId);

        await using var scope2 = restarted.CreateAsyncScope();
        var dbContext = scope2.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var repair = await dbContext.Attempts.AsNoTracking().SingleAsync(a => a.Id == repairId);
        Assert.Equal(seeded.SecondResolverAttemptId, repair.AgentRepairSourceAttemptId);
        Assert.Equal(AgentOutcome.Resolved, repair.AgentOutcome);

        // A repair adds no challenge round: the run still has exactly its two critical reviews.
        Assert.Equal(2, dbContext.Attempts.Count(a => a.RunId == seeded.RunId && a.AgentRole == AgentRole.CriticalReviewer));
        Assert.Single(dbContext.CollaborationMessages.Where(m => m.RunId == seeded.RunId && m.Type == CollaborationMessageType.Escalation));

        var manifest = await _artifactStore.VerifyAndReadSealedAsync(
            validAdapter.LastRequest!.ContextManifestRelativeStoragePath, validAdapter.LastRequest.ContextManifestByteLength,
            validAdapter.LastRequest.ContextManifestContentHash, 0, 32 * 1024, CancellationToken.None);
        Assert.Equal(SealedReadStatus.Ok, manifest.Status);
        Assert.Contains("formatRepairNotice", manifest.Text, StringComparison.Ordinal);
        Assert.Contains(seeded.FirstRevisionId.ToString(), manifest.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(seeded.SecondResolverAttemptId.ToString(), manifest.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(InvalidSecondResolutionJson, manifest.Text, StringComparison.Ordinal);

        // The single repair is consumed and a further request creates no second escalation path.
        var again = await scope2.ServiceProvider.GetRequiredService<IApplicationMediator>().SendAsync(
            CreateChallengeResolutionAttemptCommand.ForRepair(seeded.RunId, seeded.SecondResolverAttemptId), CancellationToken.None);
        Assert.Equal("agent_attempts.repair_already_requested", Assert.Single(again.Errors).Code);
    }
}
