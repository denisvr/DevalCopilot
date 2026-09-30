using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Application.Features.Runs.Queries.GetEligibleImplementationAttempts;
using DevalCopilot.Application.Features.Runs.Queries.GetEligibleReviewCorrectionAttempts;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>The effective dispatch boundary (the eligibility feeds and <c>MarkAgentAttemptDispatched</c>) for a
/// cap-bearing mutation attempt: the complete persisted provenance tuple must be coherent, and a stored limit must
/// be an exact canonical whole number, before any provider process can start. Corruption is written with raw SQL,
/// the way an out-of-band edit would appear, beside healthy sibling rows.</summary>
public sealed class ClaudeMutationTurnLimitDispatchBoundaryTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    public enum Path
    {
        Implementation,
        Correction,
    }

    public static IEnumerable<object[]> Paths => [[Path.Implementation], [Path.Correction]];

    public static IEnumerable<object[]> IncoherentProvenance()
    {
        foreach (var path in new[] { Path.Implementation, Path.Correction })
        {
            yield return [path, "UPDATE attempts SET AgentProvider = 'Codex'"];
            yield return [path, "UPDATE attempts SET AgentPermissionProfile = 'ReadOnly'"];
            yield return [path, "UPDATE attempts SET AgentPermissionProfile = 'Unknown'"];
            yield return [path, "UPDATE attempts SET AgentPermissionProfile = NULL"];
            yield return [path, "UPDATE attempts SET AgentAdapterContractVersion = NULL"];
            yield return [path, "UPDATE attempts SET AgentAdapterContractVersion = ''"];
            yield return [path, "UPDATE attempts SET AgentAdapterContractVersion = 'claude-unknown-v3'"];
            yield return [path, path == Path.Implementation
                ? "UPDATE attempts SET AgentAdapterContractVersion = 'claude-implementation-v1'"
                : "UPDATE attempts SET AgentAdapterContractVersion = 'claude-review-correction-v1'"];
            yield return [path, path == Path.Implementation
                ? "UPDATE attempts SET AgentAdapterContractVersion = 'claude-review-correction-v2'"
                : "UPDATE attempts SET AgentAdapterContractVersion = 'claude-implementation-v2'"];
        }
    }

    public static IEnumerable<object[]> MalformedStoredValues()
    {
        foreach (var path in new[] { Path.Implementation, Path.Correction })
        {
            foreach (var value in new object[] { 3.5, 4294967297L, "abc", 0, -3, 101, new byte[] { 0x37 }, "blob:37", "b:37", double.PositiveInfinity })
            {
                yield return [path, value];
            }
        }
    }

    [Theory]
    [MemberData(nameof(IncoherentProvenance))]
    public async Task A_cap_beside_incoherent_persisted_provenance_is_never_eligible_or_dispatchable(Path path, string tamperSql)
    {
        var (_, attemptId) = await SeedAsync(path, 7);
        await ExecuteAsync(tamperSql);

        await AssertNotEligibleAndNotDispatchableAsync(path, attemptId);
    }

    [Theory]
    [MemberData(nameof(MalformedStoredValues))]
    public async Task A_malformed_stored_cap_is_never_eligible_or_dispatchable_and_never_breaks_a_healthy_sibling(Path path, object stored)
    {
        var (_, healthyAttemptId) = await SeedAsync(path, 9);
        var (_, malformedAttemptId) = await SeedAsync(path, 7);
        await ClaudeMutationTurnLimitTestSupport.SetRawAttemptLimitAsync(_fixture, malformedAttemptId, stored);

        var eligible = await EligibleAsync(path);

        var healthy = Assert.Single(eligible);
        Assert.Equal(healthyAttemptId, healthy.AttemptId);
        Assert.Equal(9, healthy.RequestedMaxTurns);
        await AssertNotDispatchableAsync(malformedAttemptId);
        Assert.Equal(
            ClaudeMutationTurnLimitTestSupport.StoredText(stored),
            await ClaudeMutationTurnLimitTestSupport.ReadAttemptRawAsync(_fixture, malformedAttemptId));
        if (stored is byte[])
        {
            Assert.Equal("blob", await ClaudeMutationTurnLimitTestSupport.ReadAttemptStorageClassAsync(_fixture, malformedAttemptId));
        }
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task Coherent_v2_null_and_v1_null_attempts_stay_eligible_and_clear_the_cap_check(Path path)
    {
        var (_, v2) = await SeedAsync(path, null);
        var (_, v1) = await SeedAsync(path, null, legacyV1: true);

        var eligible = await EligibleAsync(path);

        Assert.Equal(new[] { v2, v1 }.Order(), eligible.Select(candidate => candidate.AttemptId).Order());
        Assert.All(eligible, candidate => Assert.Null(candidate.RequestedMaxTurns));
        foreach (var attemptId in new[] { v2, v1 })
        {
            await using var context = _fixture.CreateContext();
            var result = await new MarkAgentAttemptDispatchedCommandHandler(context, new FixedTimeProvider(Now))
                .HandleAsync(new MarkAgentAttemptDispatchedCommand(await RunIdAsync(attemptId), attemptId), CancellationToken.None);
            Assert.DoesNotContain(result.Errors, error => error.Code == "agent_attempts.invalid_agent_contract");
        }
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task A_coherent_cap_bearing_attempt_is_eligible_with_its_own_cap(Path path)
    {
        var (_, attemptId) = await SeedAsync(path, 7);

        var candidate = Assert.Single(await EligibleAsync(path));

        Assert.Equal(attemptId, candidate.AttemptId);
        Assert.Equal(7, candidate.RequestedMaxTurns);
    }

    private async Task AssertNotEligibleAndNotDispatchableAsync(Path path, Guid attemptId)
    {
        Assert.Empty(await EligibleAsync(path));
        await AssertNotDispatchableAsync(attemptId);
    }

    private async Task AssertNotDispatchableAsync(Guid attemptId)
    {
        await using var context = _fixture.CreateContext();
        var runId = await RunIdAsync(attemptId);
        var result = await new MarkAgentAttemptDispatchedCommandHandler(context, new FixedTimeProvider(Now))
            .HandleAsync(new MarkAgentAttemptDispatchedCommand(runId, attemptId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.invalid_agent_contract", Assert.Single(result.Errors).Code);
        await using var verify = _fixture.CreateContext();
        Assert.Null((await verify.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attemptId)).AgentDispatchedAtUtc);
    }

    private async Task<IReadOnlyList<EligibleFeedItem>> EligibleAsync(Path path)
    {
        await using var context = _fixture.CreateContext();
        if (path == Path.Implementation)
        {
            var implementation = await new GetEligibleImplementationAttemptsQueryHandler(context)
                .HandleAsync(new GetEligibleImplementationAttemptsQuery(), CancellationToken.None);
            return [.. implementation.Select(item => new EligibleFeedItem(item.AttemptId, item.RequestedMaxTurns))];
        }

        var correction = await new GetEligibleReviewCorrectionAttemptsQueryHandler(context)
            .HandleAsync(new GetEligibleReviewCorrectionAttemptsQuery(), CancellationToken.None);
        return [.. correction.Select(item => new EligibleFeedItem(item.AttemptId, item.RequestedMaxTurns))];
    }

    private sealed record EligibleFeedItem(Guid AttemptId, int? RequestedMaxTurns);

    private async Task<Guid> RunIdAsync(Guid attemptId)
    {
        await using var context = _fixture.CreateContext();
        return await context.Attempts.AsNoTracking().Where(candidate => candidate.Id == attemptId)
            .Select(candidate => candidate.RunId).SingleAsync();
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var context = _fixture.CreateContext();
        await context.Database.ExecuteSqlRawAsync(sql);
    }

    private async Task<(Guid RunId, Guid AttemptId)> SeedAsync(Path path, int? limit, bool legacyV1 = false)
    {
        await using var dbContext = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Objective", Now);
        run.Claim(Now);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        workspace.MarkReady();
        var physicalIdentity = project.Id.ToByteArray();
        var lease = RepositoryMutationLease.Acquire(
            Guid.NewGuid(), project.Id, workspace.Id, BitConverter.ToUInt64(physicalIdentity), physicalIdentity, Now);
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), Fingerprint, []);
        var manifestId = Guid.NewGuid();
        var attempt = (path, legacyV1) switch
        {
            (Path.Implementation, true) => Attempt.ClaimAgentImplementation(
                Guid.NewGuid(), run.Id, 1, workspace.Id, checkpoint.Id, Fingerprint, manifestId,
                TimeSpan.FromMinutes(20), 262144, 524288, Now, 1),
            (Path.Implementation, false) => Attempt.ClaimAgentImplementationWithAssignment(
                Guid.NewGuid(), run.Id, 1, workspace.Id, checkpoint.Id, Fingerprint, manifestId,
                TimeSpan.FromMinutes(20), 262144, 524288, Now, null, null, AgentPermissionProfile.WorkspaceEditOnly,
                ClaudeMutationAdapterContract.ImplementationV2, 1, limit),
            (_, true) => Attempt.ClaimAgentReviewCorrection(
                Guid.NewGuid(), run.Id, 1, workspace.Id, checkpoint.Id, Fingerprint, manifestId,
                TimeSpan.FromMinutes(20), 262144, 524288, Now, 1),
            _ => Attempt.ClaimAgentReviewCorrectionWithModelRequest(
                Guid.NewGuid(), run.Id, 1, workspace.Id, checkpoint.Id, Fingerprint, manifestId,
                TimeSpan.FromMinutes(20), 262144, 524288, Now, null, null, 1, limit),
        };

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.GitWorkspaces.Add(workspace);
        dbContext.RepositoryMutationLeases.Add(lease);
        dbContext.GitCheckpoints.Add(checkpoint);
        dbContext.Attempts.Add(attempt);
        dbContext.AttemptInputMessages.AddRange(
            AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, Guid.NewGuid(), 0),
            AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, Guid.NewGuid(), 1),
            AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, Guid.NewGuid(), 2));
        dbContext.Artifacts.Add(Artifact.Record(
            manifestId, run.Id, attempt.Id, ArtifactPurpose.AgentContextManifest, "application/json",
            $"runs/{run.Id}/attempts/{attempt.Id}/manifest.sealed", "sha256:manifest", 256, false,
            ArtifactCaptureOutcome.Captured, ArtifactSensitivity.HostConstructedContent,
            ArtifactRetentionPolicy.RetainUntilRunDeleted, Now));
        await dbContext.SaveChangesAsync(CancellationToken.None);
        return (run.Id, attempt.Id);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
