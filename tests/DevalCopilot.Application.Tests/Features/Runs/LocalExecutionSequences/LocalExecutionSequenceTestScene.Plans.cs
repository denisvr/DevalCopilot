using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.AuthorizePlanningImplementation;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;

/// <summary>Plan and recipe builders of the scene. Every plan is real, durable lineage evidence (the same rows the planning slices
/// leave), bound to the scene's starting workspace, checkpoint and fingerprint; none is saved half-built.</summary>
internal sealed partial class LocalExecutionSequenceTestScene
{
    /// <summary>A seeded plan: the implementable Proposal and, where there is one, the Acceptance or Human grant that authorizes it.</summary>
    public sealed record SeededPlan(
        Guid ProposalId, Guid? AcceptanceId = null, Guid? GrantId = null, Guid? OtherProposalId = null, Guid? ReviewAttemptId = null);

    /// <summary>An explicitly Accepted Planner root: its Proposal and the Accepted review's Acceptance.</summary>
    public async Task<SeededPlan> AddAcceptedRootPlanAsync()
    {
        await using var context = Fixture.CreateContext();
        var seeder = Seeder(context);
        var (_, root) = seeder.AddRoot();
        var review = seeder.AddReview(root, AgentOutcome.Accepted);
        await SaveAsync(context, seeder);
        return new SeededPlan(root.Id, review.Outputs[0].Id);
    }

    /// <summary>An Accepted depth-one revision with its exact Acceptance (the root was challenged and resolved first).</summary>
    public async Task<SeededPlan> AddAcceptedRevisionPlanAsync()
    {
        await using var context = Fixture.CreateContext();
        var seeder = Seeder(context);
        var (_, root) = seeder.AddRoot();
        var round = seeder.AddChallengedRound(root).Resolution;
        var review = seeder.AddReview(round.RevisedProposal, AgentOutcome.Accepted);
        await SaveAsync(context, seeder);
        return new SeededPlan(round.RevisedProposal.Id, review.Outputs[0].Id, OtherProposalId: root.Id);
    }

    /// <summary>An UNREVIEWED depth-one revision: an ordinary manual implementation admits it, the new opt-in surface must not.</summary>
    public async Task<SeededPlan> AddUnreviewedRevisionPlanAsync()
    {
        await using var context = Fixture.CreateContext();
        var seeder = Seeder(context);
        var (_, root) = seeder.AddRoot();
        var round = seeder.AddChallengedRound(root).Resolution;
        await SaveAsync(context, seeder);
        return new SeededPlan(round.RevisedProposal.Id, OtherProposalId: root.Id);
    }

    /// <summary>A Planner root whose review CHALLENGED it (never resolved): a challenged plan.</summary>
    public async Task<SeededPlan> AddChallengedPlanAsync()
    {
        await using var context = Fixture.CreateContext();
        var seeder = Seeder(context);
        var (_, root) = seeder.AddRoot();
        var review = seeder.AddReview(root, AgentOutcome.Challenged);
        await SaveAsync(context, seeder);
        return new SeededPlan(root.Id, ReviewAttemptId: review.Attempt.Id);
    }

    /// <summary>An Accepted Planner root followed by a NEWER Planner proposal. Like an ordinary implementation claim, the Accepted root
    /// itself stays eligible: only a revision lineage or a Human grant is superseded by a later root.</summary>
    public async Task<SeededPlan> AddAcceptedRootWithNewerProposalAsync()
    {
        await using var context = Fixture.CreateContext();
        var seeder = Seeder(context);
        var (_, root) = seeder.AddRoot();
        var review = seeder.AddReview(root, AgentOutcome.Accepted);
        var (_, newer) = seeder.AddRoot();
        await SaveAsync(context, seeder);
        return new SeededPlan(root.Id, review.Outputs[0].Id, OtherProposalId: newer.Id);
    }

    /// <summary>Records one more, independent provider-observed Planner proposal after every plan already seeded.</summary>
    public async Task<Guid> AddNewerPlannerProposalAsync()
    {
        await using var context = Fixture.CreateContext();
        var seeder = Seeder(context);
        var (_, newer) = seeder.AddRoot();
        await SaveAsync(context, seeder);
        return newer.Id;
    }

    /// <summary>ADR-0016's escalated depth-two plan with its recorded Human authorization (the grant is unconsumed).</summary>
    public async Task<SeededPlan> AddHumanAuthorizedDepthTwoPlanAsync()
    {
        await using var context = Fixture.CreateContext();
        var seeder = Seeder(context);
        var (_, root) = seeder.AddRoot();
        var (_, first) = seeder.AddChallengedRound(root);
        var (secondReview, second) = seeder.AddChallengedRound(first.RevisedProposal);
        await context.SaveChangesAsync(CancellationToken.None);
        var escalation = PlanningEscalation.Record(
            RunId, root.Id, first.RevisedProposal.Id, second.RevisedProposal.Id, secondReview.Outputs.Select(c => c.Id).ToArray(), Start);
        context.CollaborationMessages.Add(escalation);
        await SaveAsync(context, seeder);

        await using var authorize = Fixture.CreateContext();
        var authorized = await new AuthorizePlanningImplementationCommandHandler(authorize, Evidence, Clock).HandleAsync(
            new AuthorizePlanningImplementationCommand(RunId, escalation.Id, "I reviewed the second-round decisions and accept this final plan."),
            CancellationToken.None);
        Assert.True(authorized.IsSuccess, authorized.IsFailure ? authorized.Errors[0].Code : null);
        await using var read = Fixture.CreateContext();
        var grant = await read.PlanningImplementationAuthorizations.AsNoTracking().SingleAsync(candidate => candidate.RunId == RunId);
        return new SeededPlan(second.RevisedProposal.Id, GrantId: grant.Id, OtherProposalId: escalation.Id);
    }

    /// <summary>Adds one enabled (or disabled) project recipe with the given number; returns its id.</summary>
    public async Task<Guid> AddRecipeAsync(int number, string? name = null, int timeoutSeconds = 300, bool enabled = true)
    {
        await using var context = Fixture.CreateContext();
        var command = VerificationCommand.Configure(
            Guid.NewGuid(), ProjectId, number, name ?? $"Verification {number}", $@"C:\tools\verify-{number}.exe", ["--check", $"{number}"],
            timeoutSeconds, enabled, Start);
        context.VerificationCommands.Add(command);
        await context.SaveChangesAsync(CancellationToken.None);
        return command.Id;
    }

    /// <summary>The project's recipes ordered by number.</summary>
    public async Task<List<VerificationCommand>> ReadRecipesAsync()
    {
        await using var context = Fixture.CreateContext();
        return (await context.VerificationCommands.AsNoTracking().Where(command => command.ProjectId == ProjectId).ToListAsync())
            .OrderBy(command => command.CommandNumber).ToList();
    }

    /// <summary>Edits an existing recipe through the Domain (a recipe edit that drifts the confirmation and the pinned set).</summary>
    public async Task EditRecipeAsync(Guid recipeId, string? executablePath = null, int? timeoutSeconds = null, bool? enabled = null)
    {
        await using var context = Fixture.CreateContext();
        var command = await context.VerificationCommands.SingleAsync(candidate => candidate.Id == recipeId);
        command.Update(
            command.Name, executablePath ?? command.ExecutablePath, command.Arguments, timeoutSeconds ?? command.TimeoutSeconds,
            enabled ?? command.IsEnabled, Clock.GetUtcNow().AddTicks(1));
        await context.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>Rewrites stored columns of one table row directly: the only way to persist malformed rows the Domain refuses to record.</summary>
    public async Task ExecuteSqlAsync(string sql, params object[] parameters)
    {
        await using var context = Fixture.CreateContext();
#pragma warning disable EF1002 // Test-owned, fixed statement text.
        await context.Database.ExecuteSqlRawAsync(sql, parameters);
#pragma warning restore EF1002
    }

    private PlanningLineageSeeder Seeder(DevalCopilotDbContext context) =>
        new(context, RunId, WorkspaceId, CheckpointId, StartFingerprint, Start, lineageNext);

    private async Task SaveAsync(DevalCopilotDbContext context, PlanningLineageSeeder seeder)
    {
        await context.SaveChangesAsync(CancellationToken.None);
        lineageNext = seeder.NextAttemptNumber;
    }
}
