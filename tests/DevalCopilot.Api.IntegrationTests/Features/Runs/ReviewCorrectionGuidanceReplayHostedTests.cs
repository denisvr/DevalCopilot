using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Application.Features.Runs.Commands.AuthorizeReviewCorrection;
using DevalCopilot.Application.Features.Runs.Commands.CreateReviewCorrectionAttempt;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>
/// A guided correction claimed through the real handlers and mediator is replayed after a restart
/// from the attempt's own sealed manifest: the provider boundary receives the sealed snapshot with
/// the accepted guidance, and nothing but that snapshot carries it to the provider.
/// </summary>
public sealed partial class ReviewCorrectionSupervisorHostedTests
{
    private const string ReplayGuidance = "Keep the correction minimal and preserve the public API.";

    [Fact]
    public async Task A_guided_correction_is_replayed_after_restart_from_its_sealed_manifest()
    {
        var evidence = new SequencedEvidence(_ => Matching());
        Guid runId;
        Guid guidedAttemptId;
        Guid findingId;
        Guid instructionMessageId;

        await using (var claimProvider = BuildProvider(evidence, new GatedAdapter(_artifactStore), new TestNotifier()))
        {
            var seed = await SeedAsync(claimProvider, maximumAgentInvocationTime: TimeSpan.FromHours(24));
            runId = seed.RunId;
            findingId = seed.FindingId;
            await using var scope = claimProvider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var now = DateTimeOffset.UtcNow;

            // Two failed corrections exhaust the review-correction budget so an authorization is required.
            var seeded = await db.Attempts.SingleAsync(item => item.Id == seed.CorrectionId);
            seeded.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, now);
            var review = await db.Attempts.SingleAsync(item => item.RunId == runId && item.AgentRole == AgentRole.CodeReviewer);
            var second = Attempt.ClaimAgentReviewCorrection(
                Guid.NewGuid(), runId, 6, review.AgentGitWorkspaceId!.Value, review.AgentGitCheckpointId!.Value, Fingerprint,
                Guid.NewGuid(), TimeSpan.FromMinutes(20), 262144, 524288, now, 6);
            second.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, now);
            db.Attempts.Add(second);
            await db.SaveChangesAsync();

            var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
            var escalated = await mediator.SendAsync(new CreateReviewCorrectionAttemptCommand(runId, review.Id), CancellationToken.None);
            Assert.True(escalated.IsSuccess, string.Join(",", escalated.Errors.Select(e => e.Code)));
            var escalation = Assert.IsType<CreateReviewCorrectionAttemptCommandResult.Escalated>(escalated.Value);
            var authorized = await mediator.SendAsync(
                new AuthorizeReviewCorrectionCommand(runId, escalation.EscalationId, ReplayGuidance), CancellationToken.None);
            Assert.True(authorized.IsSuccess, string.Join(",", authorized.Errors.Select(e => e.Code)));
            instructionMessageId = authorized.Value.HumanInstructionMessageId;
            var claimed = await mediator.SendAsync(new CreateReviewCorrectionAttemptCommand(runId, review.Id), CancellationToken.None);
            Assert.True(claimed.IsSuccess, string.Join(",", claimed.Errors.Select(e => e.Code)));
            guidedAttemptId = Assert.IsType<CreateReviewCorrectionAttemptCommandResult.AttemptCreated>(claimed.Value).AttemptId;
        }

        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_databasePath}"));

        // A process restart: new provider and adapter; the claimed correction is still Running and undispatched.
        var adapter = new GatedAdapter(_artifactStore) { FindingId = findingId };
        // After the restart the first capture is the supervisor's pre-dispatch check; the recapture after the invocation differs.
        var restartEvidence = new SequencedEvidence(call => call <= 1 ? Matching() : Changed());
        await using var restartedProvider = BuildProvider(restartEvidence, adapter, new TestNotifier());
        var supervisor = CreateSupervisor(restartedProvider, adapter, restartEvidence);
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            await adapter.Invoked.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var request = adapter.LastRequest!;
            Assert.Equal(guidedAttemptId, request.AttemptId);

            var manifest = await _artifactStore.VerifyAndReadSealedAsync(
                request.ContextManifestRelativeStoragePath, request.ContextManifestByteLength, request.ContextManifestContentHash,
                0, 64 * 1024, CancellationToken.None);
            Assert.Equal(SealedReadStatus.Ok, manifest.Status);
            Assert.Contains(ReplayGuidance, manifest.Text, StringComparison.Ordinal);
            Assert.Contains(instructionMessageId.ToString(), manifest.Text, StringComparison.Ordinal);
            Assert.Contains("humanGuidanceBoundary", manifest.Text, StringComparison.Ordinal);

            // The dispatch contract carries no guidance or authorization of its own.
            Assert.DoesNotContain(
                typeof(ReviewCorrectionInvocationRequest).GetProperties(),
                property => property.Name.Contains("Guidance", StringComparison.OrdinalIgnoreCase)
                    || property.Name.Contains("Authorization", StringComparison.OrdinalIgnoreCase));

            adapter.Release();
            await WaitForStatusAsync(restartedProvider, guidedAttemptId, AttemptStatus.Completed);
        }
        finally
        {
            await supervisor.StopAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
        }

        Assert.Equal(1, adapter.InvocationCount);
        await using var verify = restartedProvider.GetRequiredService<IDbContextFactory<DevalCopilotDbContext>>().CreateDbContext();
        var inputs = await verify.AttemptInputMessages.Where(input => input.AttemptId == guidedAttemptId)
            .OrderBy(input => input.Sequence).Select(input => input.CollaborationMessageId).ToListAsync();
        Assert.Equal(2, inputs.Count);
        Assert.DoesNotContain(instructionMessageId, inputs);
        Assert.NotNull((await verify.ReviewCorrectionAuthorizations.SingleAsync()).ConsumedByAttemptId);
    }
}
