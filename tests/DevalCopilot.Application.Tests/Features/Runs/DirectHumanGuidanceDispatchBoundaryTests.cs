using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Application.Features.Runs.Queries.GetEligibleImplementationAttempts;
using DevalCopilot.Application.Features.Runs.Queries.GetEligibleReviewCorrectionAttempts;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>The effective dispatch boundary (both eligibility feeds and the authoritative <c>MarkAgentAttemptDispatched</c>
/// gate) for the direct-guidance snapshot of a mutation attempt: only well-formed text beside the complete coherent v2
/// tuple is eligible, the snapshot the caller expects must equal the freshly persisted one at the dispatch commit, and
/// historical unguided attempts are unaffected. Corruption is written with raw SQL beside healthy siblings.</summary>
public sealed class DirectHumanGuidanceDispatchBoundaryTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);
    private const string Guidance = "Reuse the existing helper.";

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
            yield return [path, "UPDATE attempts SET AgentPermissionProfile = NULL"];
            yield return [path, "UPDATE attempts SET AgentAdapterContractVersion = NULL"];
            yield return [path, "UPDATE attempts SET AgentAdapterContractVersion = 'claude-unknown-v3'"];
            yield return [path, path == Path.Implementation
                ? "UPDATE attempts SET AgentAdapterContractVersion = 'claude-implementation-v1'"
                : "UPDATE attempts SET AgentAdapterContractVersion = 'claude-review-correction-v1'"];
            yield return [path, path == Path.Implementation
                ? "UPDATE attempts SET AgentAdapterContractVersion = 'claude-review-correction-v2'"
                : "UPDATE attempts SET AgentAdapterContractVersion = 'claude-implementation-v2'"];
        }
    }

    public static IEnumerable<object[]> MalformedStoredText()
    {
        foreach (var path in new[] { Path.Implementation, Path.Correction })
        {
            foreach (var value in new[] { "", "   ", " padded ", "cafe\u0301", "line\r\nbreak", "tab\there", new string('x', 601), "the password is x" })
            {
                yield return [path, value];
            }
        }
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task Both_feeds_project_the_claimed_text_or_none_and_a_legacy_attempt_stays_eligible(Path path)
    {
        var (_, guided) = await SeedAsync(path, Guidance);
        var (_, plain) = await SeedAsync(path, null);
        var (_, legacy) = await SeedAsync(path, null, legacyV1: true);

        var eligible = await EligibleAsync(path);

        Assert.Equal(3, eligible.Count);
        Assert.Equal(Guidance, eligible.Single(item => item.AttemptId == guided).DirectGuidance);
        Assert.Null(eligible.Single(item => item.AttemptId == plain).DirectGuidance);
        Assert.Null(eligible.Single(item => item.AttemptId == legacy).DirectGuidance);
    }

    [Theory]
    [MemberData(nameof(IncoherentProvenance))]
    public async Task Guidance_beside_incoherent_persisted_provenance_is_never_eligible_or_dispatchable(Path path, string tamperSql)
    {
        var (_, attemptId) = await SeedAsync(path, Guidance);
        await ExecuteAsync(tamperSql);

        Assert.Empty(await EligibleAsync(path));
        await AssertNotDispatchableAsync(attemptId, new ExpectedDirectHumanGuidance(Guidance));
    }

    [Theory]
    [MemberData(nameof(MalformedStoredText))]
    public async Task Malformed_stored_text_is_never_eligible_or_dispatchable_and_never_breaks_a_healthy_sibling(Path path, string stored)
    {
        var (_, healthy) = await SeedAsync(path, "Healthy sibling guidance.");
        var (_, malformed) = await SeedAsync(path, Guidance);
        await SetRawGuidanceAsync(malformed, stored);

        var eligible = await EligibleAsync(path);

        var item = Assert.Single(eligible);
        Assert.Equal(healthy, item.AttemptId);
        Assert.Equal("Healthy sibling guidance.", item.DirectGuidance);
        await AssertNotDispatchableAsync(malformed, new ExpectedDirectHumanGuidance(stored));
        Assert.Equal(stored, await ReadRawGuidanceAsync(malformed));
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task Guidance_written_beside_a_v1_contract_is_not_eligible_or_dispatchable(Path path)
    {
        var (_, v1) = await SeedAsync(path, null, legacyV1: true);
        await SetRawGuidanceAsync(v1, Guidance);

        Assert.Empty(await EligibleAsync(path));
        await AssertNotDispatchableAsync(v1, new ExpectedDirectHumanGuidance(Guidance));
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task Dispatch_commits_only_when_the_fresh_snapshot_equals_the_expected_one(Path path)
    {
        var (runId, guided) = await SeedAsync(path, Guidance);
        var (_, plain) = await SeedAsync(path, null);

        await AssertDispatchedAsync(runId, guided, new ExpectedDirectHumanGuidance(Guidance));
        await AssertDispatchedAsync(await RunIdAsync(plain), plain, new ExpectedDirectHumanGuidance(null));
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task Dispatch_refuses_a_different_expected_snapshot_and_a_guided_attempt_without_any_expectation(Path path)
    {
        var (_, guided) = await SeedAsync(path, Guidance);
        var (_, plain) = await SeedAsync(path, null);

        await AssertMismatchAsync(guided, new ExpectedDirectHumanGuidance(null));
        await AssertMismatchAsync(guided, new ExpectedDirectHumanGuidance("A different text."));
        await AssertMismatchAsync(guided, null);
        await AssertMismatchAsync(plain, new ExpectedDirectHumanGuidance(Guidance));
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task Historical_unguided_and_authorized_style_attempts_dispatch_without_any_expectation(Path path)
    {
        var (runId, plain) = await SeedAsync(path, null);
        var (_, legacy) = await SeedAsync(path, null, legacyV1: true);

        await AssertDispatchedAsync(runId, plain, null);
        await AssertDispatchedAsync(await RunIdAsync(legacy), legacy, null);
    }

    [Theory]
    [InlineData(Path.Implementation, "Changed after the projection.")]
    [InlineData(Path.Correction, "Changed after the projection.")]
    [InlineData(Path.Implementation, null)]
    [InlineData(Path.Correction, null)]
    public async Task A_snapshot_changed_by_another_connection_after_the_context_was_populated_refuses_dispatch(Path path, string? changedTo)
    {
        var (runId, attemptId) = await SeedAsync(path, Guidance);
        await using var context = _fixture.CreateContext();

        // The handler's own context already tracks the attempt with the original snapshot (an earlier projection).
        var tracked = await context.Attempts.SingleAsync(candidate => candidate.Id == attemptId);
        Assert.Equal(Guidance, tracked.ReadAgentDirectHumanGuidance().Text);
        await SetRawGuidanceAsync(attemptId, changedTo);

        var result = await new MarkAgentAttemptDispatchedCommandHandler(context, new FixedTimeProvider(Now))
            .HandleAsync(
                new MarkAgentAttemptDispatchedCommand(runId, attemptId, new ExpectedDirectHumanGuidance(Guidance)),
                CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.direct_guidance_mismatch", Assert.Single(result.Errors).Code);
        await using var verify = _fixture.CreateContext();
        Assert.Null((await verify.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attemptId)).AgentDispatchedAtUtc);
        Assert.Equal(changedTo, await ReadRawGuidanceAsync(attemptId));
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task The_same_populated_context_dispatches_when_nothing_drifted(Path path)
    {
        var (runId, attemptId) = await SeedAsync(path, Guidance);
        await using var context = _fixture.CreateContext();
        await context.Attempts.SingleAsync(candidate => candidate.Id == attemptId);

        var result = await new MarkAgentAttemptDispatchedCommandHandler(context, new FixedTimeProvider(Now))
            .HandleAsync(
                new MarkAgentAttemptDispatchedCommand(runId, attemptId, new ExpectedDirectHumanGuidance(Guidance)),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    public static IEnumerable<object[]> CompetingAssignmentChanges()
    {
        foreach (var path in new[] { Path.Implementation, Path.Correction })
        {
            yield return [path, "UPDATE attempts SET AgentProvider = 'Codex'"];
            yield return [path, "UPDATE attempts SET AgentRole = 'CodeReviewer'"];
            yield return [path, "UPDATE attempts SET AgentPermissionProfile = 'ReadOnly'"];
            yield return [path, "UPDATE attempts SET AgentPermissionProfile = NULL"];
            yield return [path, "UPDATE attempts SET AgentResponseContract = 'Proposal'"];
            yield return [path, "UPDATE attempts SET AgentAdapterContractVersion = NULL"];
            yield return [path, "UPDATE attempts SET AgentAdapterContractVersion = 'claude-unknown-v3'"];
            yield return [path, path == Path.Implementation
                ? "UPDATE attempts SET AgentAdapterContractVersion = 'claude-implementation-v1'"
                : "UPDATE attempts SET AgentAdapterContractVersion = 'claude-review-correction-v1'"];
            yield return [path, path == Path.Implementation
                ? "UPDATE attempts SET AgentAdapterContractVersion = 'claude-review-correction-v2'"
                : "UPDATE attempts SET AgentAdapterContractVersion = 'claude-implementation-v2'"];
        }
    }

    [Theory]
    [MemberData(nameof(CompetingAssignmentChanges))]
    public async Task A_competing_assignment_change_committed_before_the_transaction_never_confers_stale_authority(Path path, string tamperSql)
    {
        var (runId, attemptId) = await SeedAsync(path, Guidance);
        await using var context = _fixture.CreateContext();

        // The handler's own context already tracks the attempt with the original, compatible assignment.
        var tracked = await context.Attempts.SingleAsync(candidate => candidate.Id == attemptId);
        Assert.True(tracked.HasDispatchCoherentDirectHumanGuidance());
        await ExecuteAsync(tamperSql);

        var result = await new MarkAgentAttemptDispatchedCommandHandler(context, new FixedTimeProvider(Now))
            .HandleAsync(
                new MarkAgentAttemptDispatchedCommand(runId, attemptId, new ExpectedDirectHumanGuidance(Guidance)),
                CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.invalid_agent_contract", Assert.Single(result.Errors).Code);
        await using var verify = _fixture.CreateContext();
        Assert.Null((await verify.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attemptId)).AgentDispatchedAtUtc);
        Assert.Equal(Guidance, await ReadRawGuidanceAsync(attemptId));
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task A_refusal_on_fresh_facts_preserves_pending_tracked_writes_and_does_not_disable_tracking(Path path)
    {
        var (runId, attemptId) = await SeedAsync(path, Guidance);
        await using var context = _fixture.CreateContext();
        var tracked = await context.Attempts.SingleAsync(candidate => candidate.Id == attemptId);
        tracked.RecordAgentObservedAssignment("opus", "high");
        await ExecuteAsync("UPDATE attempts SET AgentProvider = 'Codex'");

        var result = await new MarkAgentAttemptDispatchedCommandHandler(context, new FixedTimeProvider(Now))
            .HandleAsync(
                new MarkAgentAttemptDispatchedCommand(runId, attemptId, new ExpectedDirectHumanGuidance(Guidance)),
                CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(EntityState.Modified, context.Entry(tracked).State);
        await context.SaveChangesAsync(CancellationToken.None);
        await using var verify = _fixture.CreateContext();
        var persisted = await verify.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attemptId);
        Assert.Equal("opus", persisted.AgentObservedModel);
        Assert.Null(persisted.AgentDispatchedAtUtc);
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task A_competing_change_that_keeps_the_assignment_compatible_still_dispatches(Path path)
    {
        var (runId, attemptId) = await SeedAsync(path, Guidance);
        await using var context = _fixture.CreateContext();
        await context.Attempts.SingleAsync(candidate => candidate.Id == attemptId);
        await ExecuteAsync("UPDATE attempts SET AgentRequestedModel = 'sonnet'");

        var result = await new MarkAgentAttemptDispatchedCommandHandler(context, new FixedTimeProvider(Now))
            .HandleAsync(
                new MarkAgentAttemptDispatchedCommand(runId, attemptId, new ExpectedDirectHumanGuidance(Guidance)),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    private async Task AssertDispatchedAsync(Guid runId, Guid attemptId, ExpectedDirectHumanGuidance? expected)
    {
        await using var context = _fixture.CreateContext();
        var result = await new MarkAgentAttemptDispatchedCommandHandler(context, new FixedTimeProvider(Now))
            .HandleAsync(new MarkAgentAttemptDispatchedCommand(runId, attemptId, expected), CancellationToken.None);
        Assert.True(result.IsSuccess, string.Join(",", result.Errors.Select(error => error.Code)));
        await context.SaveChangesAsync(CancellationToken.None);
        await using var verify = _fixture.CreateContext();
        Assert.NotNull((await verify.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attemptId)).AgentDispatchedAtUtc);
    }

    private async Task AssertMismatchAsync(Guid attemptId, ExpectedDirectHumanGuidance? expected)
    {
        await using var context = _fixture.CreateContext();
        var result = await new MarkAgentAttemptDispatchedCommandHandler(context, new FixedTimeProvider(Now))
            .HandleAsync(new MarkAgentAttemptDispatchedCommand(await RunIdAsync(attemptId), attemptId, expected), CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.direct_guidance_mismatch", Assert.Single(result.Errors).Code);
        await using var verify = _fixture.CreateContext();
        Assert.Null((await verify.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attemptId)).AgentDispatchedAtUtc);
    }

    private async Task AssertNotDispatchableAsync(Guid attemptId, ExpectedDirectHumanGuidance? expected)
    {
        await using var context = _fixture.CreateContext();
        var result = await new MarkAgentAttemptDispatchedCommandHandler(context, new FixedTimeProvider(Now))
            .HandleAsync(new MarkAgentAttemptDispatchedCommand(await RunIdAsync(attemptId), attemptId, expected), CancellationToken.None);

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
            return [.. implementation.Select(item => new EligibleFeedItem(item.AttemptId, item.DirectHumanGuidance))];
        }

        var correction = await new GetEligibleReviewCorrectionAttemptsQueryHandler(context)
            .HandleAsync(new GetEligibleReviewCorrectionAttemptsQuery(), CancellationToken.None);
        return [.. correction.Select(item => new EligibleFeedItem(item.AttemptId, item.DirectHumanGuidance))];
    }

    private sealed record EligibleFeedItem(Guid AttemptId, string? DirectGuidance);

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

    private async Task SetRawGuidanceAsync(Guid attemptId, string? value)
    {
        await using var context = _fixture.CreateContext();
        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE attempts SET AgentDirectHumanGuidance = {value} WHERE Id = {attemptId}");
    }

    private async Task<string?> ReadRawGuidanceAsync(Guid attemptId)
    {
        await using var context = _fixture.CreateContext();
        return await context.Database
            .SqlQuery<string?>($"SELECT AgentDirectHumanGuidance AS Value FROM attempts WHERE Id = {attemptId}")
            .SingleAsync();
    }

    private async Task<(Guid RunId, Guid AttemptId)> SeedAsync(Path path, string? guidance, bool legacyV1 = false)
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
                ClaudeMutationAdapterContract.ImplementationV2, 1, null, guidance),
            (_, true) => Attempt.ClaimAgentReviewCorrection(
                Guid.NewGuid(), run.Id, 1, workspace.Id, checkpoint.Id, Fingerprint, manifestId,
                TimeSpan.FromMinutes(20), 262144, 524288, Now, 1),
            _ => Attempt.ClaimAgentReviewCorrectionWithModelRequest(
                Guid.NewGuid(), run.Id, 1, workspace.Id, checkpoint.Id, Fingerprint, manifestId,
                TimeSpan.FromMinutes(20), 262144, 524288, Now, null, null, 1, null, guidance),
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
