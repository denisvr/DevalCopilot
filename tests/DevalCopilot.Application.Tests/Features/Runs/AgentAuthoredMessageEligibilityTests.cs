using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// Direct regression coverage for <see cref="AgentAuthoredMessageEligibility.ResolveOwningAttemptAsync"/>:
/// its public contract is "eligible Attempt or null," never an exception caused by malformed
/// persisted state. Every failure mode is exercised via a real attempt/message pair, corrupted only
/// through the documented test-only reflection helpers (<see cref="AttemptProviderSubstitution"/>
/// and a local helper for <see cref="Attempt.AgentResponseContract"/>) — never a production bypass.
/// </summary>
public sealed class AgentAuthoredMessageEligibilityTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private static void SetResponseContract(Attempt attempt, AgentResponseContract responseContract)
    {
        var property = typeof(Attempt).GetProperty(nameof(Attempt.AgentResponseContract))
            ?? throw new InvalidOperationException("Attempt has no AgentResponseContract property.");
        var setter = property.GetSetMethod(nonPublic: true)
            ?? throw new InvalidOperationException("Attempt.AgentResponseContract has no setter.");
        setter.Invoke(attempt, [responseContract]);
    }

    private async Task<(Run Run, Attempt PlannerAttempt, CollaborationMessage Proposal)> SeedPlannerProposalAsync(
        DevalCopilot.Infrastructure.Persistence.DevalCopilotDbContext dbContext)
    {
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Plan the next increment", Now);
        run.Claim(Now);
        var attempt = Attempt.ClaimAgent(
            Guid.NewGuid(), run.Id, 1, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now, 1);
        attempt.MarkAgentDispatched(Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var proposal = CollaborationMessage.RecordAgent(
            attempt, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), CollaborationMessageType.Proposal, null,
            "A proposal.",
            System.Text.Json.JsonSerializer.Serialize(new
            {
                scope = "Ledger",
                implementationSteps = "Add the table then the query",
                risks = "Unbounded content",
                verificationPlan = "Tests",
                escalationPoints = "None expected",
            }),
            Now);
        dbContext.CollaborationMessages.Add(proposal);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return (run, attempt, proposal);
    }

    [Fact]
    public async Task ResolveOwningAttemptAsync_returns_the_attempt_for_a_valid_message()
    {
        await using var dbContext = _fixture.CreateContext();
        var (run, attempt, proposal) = await SeedPlannerProposalAsync(dbContext);

        var result = await AgentAuthoredMessageEligibility.ResolveOwningAttemptAsync(
            dbContext, proposal, run.Id, AgentRole.Planner, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(attempt.Id, result!.Id);
    }

    [Fact]
    public async Task ResolveOwningAttemptAsync_returns_null_without_throwing_for_a_null_provider()
    {
        await using var dbContext = _fixture.CreateContext();
        var (run, attempt, proposal) = await SeedPlannerProposalAsync(dbContext);
        AttemptProviderSubstitution.SetProvider(attempt, (AgentProvider?)null);

        var result = await AgentAuthoredMessageEligibility.ResolveOwningAttemptAsync(
            dbContext, proposal, run.Id, AgentRole.Planner, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveOwningAttemptAsync_returns_null_without_throwing_for_an_undefined_provider()
    {
        await using var dbContext = _fixture.CreateContext();
        var (run, attempt, proposal) = await SeedPlannerProposalAsync(dbContext);
        AttemptProviderSubstitution.SetUndefinedProvider(attempt);

        var result = await AgentAuthoredMessageEligibility.ResolveOwningAttemptAsync(
            dbContext, proposal, run.Id, AgentRole.Planner, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveOwningAttemptAsync_returns_null_for_a_mismatched_response_contract()
    {
        await using var dbContext = _fixture.CreateContext();
        var (run, attempt, proposal) = await SeedPlannerProposalAsync(dbContext);
        SetResponseContract(attempt, AgentResponseContract.CriticalReview);

        var result = await AgentAuthoredMessageEligibility.ResolveOwningAttemptAsync(
            dbContext, proposal, run.Id, AgentRole.Planner, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveOwningAttemptAsync_returns_null_for_an_actor_provider_mismatch()
    {
        await using var dbContext = _fixture.CreateContext();
        var (run, attempt, _) = await SeedPlannerProposalAsync(dbContext);
        // attempt.AgentProvider remains Codex, but this message's own Actor is Claude — a
        // divergence that must never be trusted, regardless of how it arose.
        var mismatchedProposal = CollaborationMessage.Record(
            Guid.NewGuid(), run.Id, attempt.Id, CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.Planner, AgentProvider.ClaudeCode), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex), CollaborationMessageType.Proposal, null,
            "A proposal.",
            System.Text.Json.JsonSerializer.Serialize(new
            {
                scope = "Ledger",
                implementationSteps = "Add the table then the query",
                risks = "Unbounded content",
                verificationPlan = "Tests",
                escalationPoints = "None expected",
            }),
            CollaborationMessageProvenance.ProviderObserved, Now);
        dbContext.CollaborationMessages.Add(mismatchedProposal);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await AgentAuthoredMessageEligibility.ResolveOwningAttemptAsync(
            dbContext, mismatchedProposal, run.Id, AgentRole.Planner, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveOwningAttemptAsync_returns_null_when_the_attempts_role_does_not_match_the_expected_role()
    {
        await using var dbContext = _fixture.CreateContext();
        var (run, attempt, proposal) = await SeedPlannerProposalAsync(dbContext);

        var result = await AgentAuthoredMessageEligibility.ResolveOwningAttemptAsync(
            dbContext, proposal, run.Id, AgentRole.CriticalReviewer, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveOwningAttemptAsync_returns_null_without_throwing_for_a_missing_role()
    {
        await using var dbContext = _fixture.CreateContext();
        var (run, attempt, proposal) = await SeedPlannerProposalAsync(dbContext);
        var roleProperty = typeof(Attempt).GetProperty(nameof(Attempt.AgentRole))!;
        roleProperty.GetSetMethod(nonPublic: true)!.Invoke(attempt, [null]);

        var result = await AgentAuthoredMessageEligibility.ResolveOwningAttemptAsync(
            dbContext, proposal, run.Id, AgentRole.Planner, CancellationToken.None);

        Assert.Null(result);
    }
}
