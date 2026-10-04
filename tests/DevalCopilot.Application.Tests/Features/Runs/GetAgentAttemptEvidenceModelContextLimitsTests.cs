using DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptEvidence;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>The bounded attempt evidence reports the provider-listed model limits recorded when a Claude attempt concluded, from
/// stored facts alone, and never for an attempt whose identity, state, or stored text cannot be trusted.</summary>
public sealed class GetAgentAttemptEvidenceModelContextLimitsTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);
    private const string Source = "claude-cli-model-usage-v1";

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<(Guid RunId, Guid AttemptId)> SeedAsync(Func<Guid, Attempt> attemptFactory)
    {
        await using var context = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "Evidence model limits", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Inspect evidence", Now);
        var attempt = attemptFactory(run.Id);
        context.Projects.Add(project);
        context.Runs.Add(run);
        context.Attempts.Add(attempt);
        await context.SaveChangesAsync();
        return (run.Id, attempt.Id);
    }

    private static Attempt ConcludedReview(Guid runId, AgentModelContextLimitsEvidence? limits)
    {
        var attempt = Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), runId, 1, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 65536, 131072, Now, 1);
        attempt.MarkAgentDispatched(Now);
        attempt.CompleteAgent(
            AgentOutcome.ProviderInvocationFailed, null, Now.AddMinutes(1), TestProcessEvidence.CleanExit, TestTokenUsage.Evidence, limits);
        return attempt;
    }

    private async Task<GetAgentAttemptEvidenceQueryResult> ReadAsync(Guid runId, Guid attemptId)
    {
        await using var context = _fixture.CreateContext();
        var result = await new GetAgentAttemptEvidenceQueryHandler(context)
            .HandleAsync(new GetAgentAttemptEvidenceQuery(runId, attemptId), CancellationToken.None);
        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Code)));
        return result.Value;
    }

    private async Task SetStoredAsync(Guid attemptId, string? stored)
    {
        await using var context = _fixture.CreateContext();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE attempts SET AgentModelContextLimitsSnapshot = {stored} WHERE Id = {attemptId}");
    }

    [Fact]
    public async Task A_concluded_attempt_reports_the_recorded_models_in_ordinal_order_beside_its_other_evidence()
    {
        var limits = AgentModelContextLimitsEvidence.Create(
            Source, [new AgentModelContextLimit("claude-b", 1000000, 64000), new AgentModelContextLimit("claude-a", 200000, 32000)]);
        var (runId, attemptId) = await SeedAsync(id => ConcludedReview(id, limits));

        var evidence = await ReadAsync(runId, attemptId);

        Assert.True(evidence.IdentityValid);
        Assert.Equal(
            [new AgentModelContextLimit("claude-a", 200000, 32000), new AgentModelContextLimit("claude-b", 1000000, 64000)],
            evidence.ModelContextLimits!.Models.AsEnumerable());
        Assert.Equal(TestTokenUsage.Evidence, evidence.TokenUsage);
        Assert.Equal(AgentOutcome.ProviderInvocationFailed, evidence.Outcome);
    }

    [Fact]
    public async Task A_concluded_attempt_without_recorded_limits_reports_none_never_a_default()
    {
        var (runId, attemptId) = await SeedAsync(id => ConcludedReview(id, null));

        var evidence = await ReadAsync(runId, attemptId);

        Assert.True(evidence.IdentityValid);
        Assert.Null(evidence.ModelContextLimits);
        Assert.Equal(TestTokenUsage.Evidence, evidence.TokenUsage);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"version\":2,\"source\":\"claude-cli-model-usage-v1\",\"models\":[{\"modelId\":\"a\",\"contextWindowTokens\":1,\"maxOutputTokens\":1}]}")]
    [InlineData("{\"version\":1,\"source\":\"other-v1\",\"models\":[{\"modelId\":\"a\",\"contextWindowTokens\":1,\"maxOutputTokens\":1}]}")]
    [InlineData("{\"version\":1,\"source\":\"claude-cli-model-usage-v1\",\"models\":[]}")]
    public async Task Malformed_stored_text_reports_none_without_failing_or_hiding_the_rest_of_the_evidence(string stored)
    {
        var (runId, attemptId) = await SeedAsync(id => ConcludedReview(id, AgentModelContextLimitsEvidence.Create(Source, [new("a", 1, 1)])));
        await SetStoredAsync(attemptId, stored);

        var evidence = await ReadAsync(runId, attemptId);

        Assert.True(evidence.IdentityValid);
        Assert.Null(evidence.ModelContextLimits);
        Assert.Equal(TestTokenUsage.Evidence, evidence.TokenUsage);
        Assert.Equal(AgentOutcome.ProviderInvocationFailed, evidence.Outcome);
    }

    [Fact]
    public async Task An_identity_that_cannot_be_proven_discloses_no_limits_even_beside_a_valid_snapshot()
    {
        var implementation = await SeedAsync(id =>
        {
            var attempt = Attempt.ClaimAgentImplementationWithAssignment(
                Guid.NewGuid(), id, 1, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(20), 65536, 131072,
                Now, null, null, AgentPermissionProfile.WorkspaceEditOnly, ClaudeMutationAdapterContract.ImplementationV2, 1, 5);
            attempt.MarkAgentDispatched(Now);
            attempt.CompleteImplementation(
                AgentOutcome.ProviderInvocationFailed, null, Now.AddMinutes(1),
                AgentProcessExecutionEvidence.Create(ProcessOutcome.Exited, 1, TimeSpan.FromSeconds(1)), null,
                AgentModelContextLimitsEvidence.Create(Source, [new("a", 1, 1)]));
            return attempt;
        });
        await ClaudeMutationTurnLimitTestSupport.SetRawAttemptLimitAsync(_fixture, implementation.AttemptId, 101);

        var evidence = await ReadAsync(implementation.RunId, implementation.AttemptId);

        Assert.False(evidence.IdentityValid);
        Assert.Null(evidence.ModelContextLimits);
    }

    [Fact]
    public async Task A_running_attempt_reports_no_limits_even_when_its_row_holds_a_canonical_snapshot()
    {
        var (runId, attemptId) = await SeedAsync(id =>
        {
            var attempt = Attempt.ClaimAgentCriticalReview(
                Guid.NewGuid(), id, 1, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 65536, 131072, Now, 1);
            attempt.MarkAgentDispatched(Now);
            return attempt;
        });
        await SetStoredAsync(attemptId, AgentModelContextLimitsEvidence.Create(Source, [new("a", 1, 1)]).Serialize());

        var evidence = await ReadAsync(runId, attemptId);

        Assert.True(evidence.IdentityValid);
        Assert.Equal(AttemptStatus.Running, evidence.AttemptStatus);
        Assert.Null(evidence.ModelContextLimits);
    }
}
