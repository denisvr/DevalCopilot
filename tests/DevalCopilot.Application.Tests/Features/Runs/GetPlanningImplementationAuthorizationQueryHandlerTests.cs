using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Queries.GetPlanningImplementationAuthorization;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.PlanningAuthorizationTestSupport;
using static DevalCopilot.Application.Tests.Features.Runs.SecondChallengeRoundTestSupport;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>The truthful read of the human implementation authorization of one planning escalation (ADR-0016): absent,
/// available, consumed, stale, or invalid, from durable facts only; unknown or foreign escalations are not found.</summary>
public sealed class GetPlanningImplementationAuthorizationQueryHandlerTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<PlanningImplementationAuthorizationQueryResult> ReadAsync(Guid runId, Guid escalationId)
    {
        await using var dbContext = _fixture.CreateContext();
        var result = await new GetPlanningImplementationAuthorizationQueryHandler(dbContext).HandleAsync(
            new GetPlanningImplementationAuthorizationQuery(runId, escalationId), CancellationToken.None);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
        return result.Value;
    }

    [Fact]
    public async Task A_valid_current_escalation_without_a_grant_is_absent_and_names_the_final_plan_and_ordered_decisions()
    {
        await using var seed = _fixture.CreateContext();
        var lineage = await SeedEscalatedLineageAsync(seed, secondChallengeCount: 2);

        var state = await ReadAsync(lineage.RunId, lineage.Escalation.Id);

        Assert.Equal(PlanningImplementationAuthorizationState.Absent, state.State);
        Assert.Equal(lineage.FinalProposal.Id, state.FinalProposalMessageId);
        Assert.Equal(lineage.Second.Decisions.Select(decision => decision.Id), state.OrderedDecisionMessageIds);
        Assert.Null(state.AuthorizationId);
        Assert.Null(state.Rationale);
    }

    [Fact]
    public async Task An_unconsumed_current_grant_is_available_with_its_exact_recorded_reason()
    {
        await using var seed = _fixture.CreateContext();
        var lineage = await SeedEscalatedLineageAsync(seed);
        var authorization = await AuthorizeAsync(seed, lineage);

        var state = await ReadAsync(lineage.RunId, lineage.Escalation.Id);

        Assert.Equal(PlanningImplementationAuthorizationState.Available, state.State);
        Assert.Equal(authorization.Value.AuthorizationId, state.AuthorizationId);
        Assert.Equal(authorization.Value.HumanInstructionMessageId, state.HumanInstructionMessageId);
        Assert.Equal(Rationale, state.Rationale);
        Assert.Null(state.ConsumedByAttemptId);
    }

    [Fact]
    public async Task A_spent_grant_is_consumed_and_names_its_attempt()
    {
        await using var seed = _fixture.CreateContext();
        var lineage = await SeedEscalatedLineageAsync(seed);
        await AuthorizeAsync(seed, lineage);
        var claim = await ClaimAsync(seed, lineage);

        var state = await ReadAsync(lineage.RunId, lineage.Escalation.Id);

        Assert.Equal(PlanningImplementationAuthorizationState.Consumed, state.State);
        Assert.Equal(claim.Value.AttemptId, state.ConsumedByAttemptId);
        Assert.Equal(Now, state.ConsumedAtUtc);
        Assert.Equal(Rationale, state.Rationale);
    }

    [Fact]
    public async Task A_newer_planner_proposal_or_checkpoint_makes_both_a_grant_and_an_absent_source_stale()
    {
        await using var seed = _fixture.CreateContext();
        var withGrant = await SeedEscalatedLineageAsync(seed);
        await AuthorizeAsync(seed, withGrant);
        await using var other = _fixture.CreateContext();
        var withoutGrant = await SeedEscalatedLineageAsync(other, seedCapabilities: false);

        SeederFor(seed, withGrant.Scene, withGrant.Seeder.NextAttemptNumber + 10).AddRoot();
        await seed.SaveChangesAsync(CancellationToken.None);
        other.GitCheckpoints.Add(GitCheckpoint.Capture(
            Guid.NewGuid(), withoutGrant.Scene.Workspace.Id, 2, Now.AddMinutes(1), new string('b', 40), new string('b', 64), []));
        await other.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(PlanningImplementationAuthorizationState.Stale, (await ReadAsync(withGrant.RunId, withGrant.Escalation.Id)).State);
        Assert.Equal(PlanningImplementationAuthorizationState.Stale, (await ReadAsync(withoutGrant.RunId, withoutGrant.Escalation.Id)).State);
    }

    [Fact]
    public async Task A_tampered_recorded_instruction_or_escalation_is_invalid_without_exposing_any_text()
    {
        await using var seed = _fixture.CreateContext();
        var withGrant = await SeedEscalatedLineageAsync(seed);
        var authorization = await AuthorizeAsync(seed, withGrant);
        await seed.CollaborationMessages.Where(message => message.Id == authorization.Value.HumanInstructionMessageId)
            .ExecuteUpdateAsync(set => set.SetProperty(message => message.StructuredContentJson, "{\"instruction\":\"x\",\"rationale\":\"leaky\"}"));
        await using var other = _fixture.CreateContext();
        var forged = await SeedEscalatedLineageAsync(other, seedCapabilities: false);
        await other.CollaborationMessages.Where(message => message.Id == forged.Escalation.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(message => message.Summary, "Forged."));

        var recorded = await ReadAsync(withGrant.RunId, withGrant.Escalation.Id);
        var source = await ReadAsync(forged.RunId, forged.Escalation.Id);

        Assert.Equal(PlanningImplementationAuthorizationState.Invalid, recorded.State);
        Assert.Null(recorded.Rationale);
        Assert.Equal(PlanningImplementationAuthorizationState.Invalid, source.State);
        Assert.Null(source.FinalProposalMessageId);
    }

    [Fact]
    public async Task Unknown_foreign_and_non_escalation_messages_and_unknown_runs_are_not_found()
    {
        await using var seed = _fixture.CreateContext();
        var lineage = await SeedEscalatedLineageAsync(seed);
        await using var other = _fixture.CreateContext();
        var foreign = await SeedEscalatedLineageAsync(other, seedCapabilities: false);
        await using var dbContext = _fixture.CreateContext();
        var handler = new GetPlanningImplementationAuthorizationQueryHandler(dbContext);

        foreach (var messageId in new[] { Guid.NewGuid(), foreign.Escalation.Id, lineage.FinalProposal.Id })
        {
            var result = await handler.HandleAsync(
                new GetPlanningImplementationAuthorizationQuery(lineage.RunId, messageId), CancellationToken.None);
            Assert.Equal(PlanningImplementationAuthorizationErrors.SourceNotFoundCode, Assert.Single(result.Errors).Code);
        }

        var unknownRun = await handler.HandleAsync(
            new GetPlanningImplementationAuthorizationQuery(Guid.NewGuid(), lineage.Escalation.Id), CancellationToken.None);
        Assert.Equal("runs.not_found", Assert.Single(unknownRun.Errors).Code);
    }

    [Fact]
    public async Task Reading_changes_nothing()
    {
        await using var seed = _fixture.CreateContext();
        var lineage = await SeedEscalatedLineageAsync(seed);

        await ReadAsync(lineage.RunId, lineage.Escalation.Id);

        await using var verify = _fixture.CreateContext();
        Assert.Empty(verify.PlanningImplementationAuthorizations);
        Assert.Empty(verify.CollaborationMessages.Where(message => message.Type == CollaborationMessageType.HumanInstruction));
    }
}
