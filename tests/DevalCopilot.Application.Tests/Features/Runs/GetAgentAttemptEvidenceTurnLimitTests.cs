using DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptEvidence;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>The bounded attempt evidence reports the immutable turn-limit record of a Claude mutation
/// attempt by exact version-aware mapping, and only for those attempts.</summary>
public sealed class GetAgentAttemptEvidenceTurnLimitTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<(Guid RunId, Guid AttemptId)> SeedAsync(Func<Guid, Attempt> attemptFactory)
    {
        await using var context = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "Evidence turn limit", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Inspect evidence", Now);
        var attempt = attemptFactory(run.Id);
        context.Projects.Add(project);
        context.Runs.Add(run);
        context.Attempts.Add(attempt);
        await context.SaveChangesAsync();
        return (run.Id, attempt.Id);
    }

    private static Attempt Implementation(Guid runId, int? limit, bool legacy = false) => legacy
        ? Attempt.ClaimAgentImplementation(
            Guid.NewGuid(), runId, 1, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(20), 65536, 131072, Now, 1)
        : Attempt.ClaimAgentImplementationWithAssignment(
            Guid.NewGuid(), runId, 1, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(20), 65536, 131072, Now, null, null, AgentPermissionProfile.WorkspaceEditOnly,
            ClaudeMutationAdapterContract.ImplementationV2, 1, limit);

    private async Task<GetAgentAttemptEvidenceQueryResult> ReadAsync(Guid runId, Guid attemptId)
    {
        await using var context = _fixture.CreateContext();
        var result = await new GetAgentAttemptEvidenceQueryHandler(context)
            .HandleAsync(new GetAgentAttemptEvidenceQuery(runId, attemptId), CancellationToken.None);
        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Code)));
        return result.Value;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(37)]
    [InlineData(100)]
    public async Task A_v2_implementation_attempt_reports_its_requested_limit(int limit)
    {
        var (runId, attemptId) = await SeedAsync(id => Implementation(id, limit));

        var evidence = await ReadAsync(runId, attemptId);

        Assert.True(evidence.IdentityValid);
        Assert.Equal(new(ClaudeMutationTurnLimitEvidence.Requested, limit), evidence.MaxTurns);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public async Task A_v2_review_correction_attempt_reports_its_requested_limit(int limit)
    {
        var (runId, attemptId) = await SeedAsync(id => Attempt.ClaimAgentReviewCorrectionWithModelRequest(
            Guid.NewGuid(), id, 1, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(20), 65536, 131072, Now, null, null, 1, limit));

        var evidence = await ReadAsync(runId, attemptId);

        Assert.True(evidence.IdentityValid);
        Assert.Equal(new(ClaudeMutationTurnLimitEvidence.Requested, limit), evidence.MaxTurns);
    }

    [Fact]
    public async Task A_v2_attempt_without_a_limit_reports_not_requested()
    {
        var (runId, attemptId) = await SeedAsync(id => Implementation(id, null));

        var evidence = await ReadAsync(runId, attemptId);

        Assert.Equal(new(ClaudeMutationTurnLimitEvidence.NotRequested, null), evidence.MaxTurns);
    }

    [Fact]
    public async Task A_legacy_v1_attempt_reports_not_recorded()
    {
        var (runId, attemptId) = await SeedAsync(id => Implementation(id, null, legacy: true));

        var evidence = await ReadAsync(runId, attemptId);

        Assert.True(evidence.IdentityValid);
        Assert.Equal(new(ClaudeMutationTurnLimitEvidence.NotRecorded, null), evidence.MaxTurns);
    }

    [Fact]
    public async Task A_limit_stored_beside_a_v1_attempt_reports_unknown()
    {
        var (runId, attemptId) = await SeedAsync(id => Implementation(id, null, legacy: true));
        await ClaudeMutationTurnLimitTestSupport.SetRawAttemptLimitAsync(_fixture, attemptId, 5);

        var evidence = await ReadAsync(runId, attemptId);

        Assert.True(evidence.IdentityValid);
        Assert.Equal(new(ClaudeMutationTurnLimitEvidence.Unknown, null), evidence.MaxTurns);
    }

    [Fact]
    public async Task A_non_mutation_attempt_has_no_turn_limit_fact()
    {
        var (runId, attemptId) = await SeedAsync(id => Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), id, 1, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 65536, 131072, Now, 1));

        var evidence = await ReadAsync(runId, attemptId);

        Assert.True(evidence.IdentityValid);
        Assert.Null(evidence.MaxTurns);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task A_malformed_out_of_range_limit_makes_the_identity_invalid_and_discloses_no_limit(int stored)
    {
        var (runId, attemptId) = await SeedAsync(id => Implementation(id, 5));
        await ClaudeMutationTurnLimitTestSupport.SetRawAttemptLimitAsync(_fixture, attemptId, stored);

        var evidence = await ReadAsync(runId, attemptId);

        Assert.False(evidence.IdentityValid);
        Assert.Null(evidence.MaxTurns);
        Assert.Null(evidence.Provider);
        Assert.Null(evidence.Role);
        Assert.Equal(stored.ToString(), await ClaudeMutationTurnLimitTestSupport.ReadAttemptRawAsync(_fixture, attemptId));
    }
}
