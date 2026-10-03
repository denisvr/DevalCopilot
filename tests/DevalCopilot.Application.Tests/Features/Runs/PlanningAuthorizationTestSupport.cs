using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.AuthorizePlanningImplementation;
using DevalCopilot.Application.Features.Runs.Commands.CreateImplementationAttempt;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using static DevalCopilot.Application.Tests.Features.Runs.SecondChallengeRoundTestSupport;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>Shared arrangement for the explicit human implementation authorization of an escalated depth-two plan
/// (ADR-0016): one real, complete two-round lineage with its canonical host escalation, seeded exactly as production
/// writes it, plus helpers to authorize and to claim.</summary>
internal static class PlanningAuthorizationTestSupport
{
    public const string Rationale = "I reviewed the second-round decisions and accept this final plan.";

    internal sealed record EscalatedLineage(
        Scene Scene,
        CollaborationMessage Root,
        PlanningLineageSeeder.Resolution First,
        PlanningLineageSeeder.Review SecondReview,
        PlanningLineageSeeder.Resolution Second,
        CollaborationMessage Escalation,
        PlanningLineageSeeder Seeder)
    {
        public Guid RunId => Scene.Run.Id;

        public CollaborationMessage FinalProposal => Second.RevisedProposal;
    }

    public static async Task<EscalatedLineage> SeedEscalatedLineageAsync(
        DevalCopilotDbContext dbContext,
        int secondChallengeCount = 1,
        bool claudeObserved = true,
        int maximumAgentAttempts = 16,
        bool seedCapabilities = true,
        EscalationForm form = EscalationForm.Writer)
    {
        var scene = await SeedSceneAsync(dbContext, claudeObserved, maximumAgentAttempts, seedCapabilities);
        var seeder = SeederFor(dbContext, scene);
        var (_, root) = seeder.AddRoot();
        var (_, first) = seeder.AddChallengedRound(root);
        var (secondReview, second) = seeder.AddChallengedRound(first.RevisedProposal, secondChallengeCount);

        // Saved before the escalation is added: an attemptless row has no pending principal, so one shared save would
        // order it ahead of the attempt-owned messages. Production writes it after the resolver's attempt exists.
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var challengeIds = secondReview.Outputs.Select(challenge => challenge.Id).ToArray();
        var escalation = form == EscalationForm.Writer
            ? PlanningEscalation.Record(
                scene.Run.Id, root.Id, first.RevisedProposal.Id, second.RevisedProposal.Id, challengeIds, Now)
            : PlanningEscalationForms.Record(
                scene.Run.Id,
                second.RevisedProposal.Id,
                PlanningEscalationForms.Of(form, root.Id, first.RevisedProposal.Id, second.RevisedProposal.Id, challengeIds),
                Now);
        dbContext.CollaborationMessages.Add(escalation);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        return new EscalatedLineage(scene, root, first, secondReview, second, escalation, seeder);
    }

    public static AuthorizePlanningImplementationCommandHandler AuthorizeHandler(
        IDevalCopilotDbContext dbContext, CountingEvidenceReader? evidence = null) =>
        new(dbContext, evidence ?? new CountingEvidenceReader(), new FixedTimeProvider(Now));

    public static CreateImplementationAttemptCommandHandler ClaimHandler(
        IDevalCopilotDbContext dbContext,
        CountingEvidenceReader? evidence = null,
        RecordingArtifactStore? store = null,
        IAttemptDurabilityProbe? probe = null) =>
        new(dbContext, evidence ?? new CountingEvidenceReader(), store ?? new RecordingArtifactStore(), new FixedTimeProvider(Now), probe);

    public static Task<Devalente.Shared.Results.Result<AuthorizePlanningImplementationCommandResult>> AuthorizeAsync(
        DevalCopilotDbContext dbContext, EscalatedLineage lineage, string rationale = Rationale) =>
        AuthorizeHandler(dbContext).HandleAsync(
            new AuthorizePlanningImplementationCommand(lineage.RunId, lineage.Escalation.Id, rationale), CancellationToken.None);

    public static Task<Devalente.Shared.Results.Result<CreateImplementationAttemptCommandResult>> ClaimAsync(
        DevalCopilotDbContext dbContext,
        EscalatedLineage lineage,
        CountingEvidenceReader? evidence = null,
        RecordingArtifactStore? store = null,
        string? guidance = null) =>
        ClaimHandler(dbContext, evidence, store).HandleAsync(
            new CreateImplementationAttemptCommand(lineage.RunId, lineage.FinalProposal.Id, guidance), CancellationToken.None);

    public static async Task<List<Guid>> InputsAsync(DevalCopilotDbContext dbContext, Guid attemptId) =>
        await dbContext.AttemptInputMessages.AsNoTracking()
            .Where(input => input.AttemptId == attemptId)
            .OrderBy(input => input.Sequence)
            .Select(input => input.CollaborationMessageId)
            .ToListAsync();

    public static Task<PlanningImplementationAuthorization> GrantAsync(DevalCopilotDbContext dbContext, Guid runId) =>
        dbContext.PlanningImplementationAuthorizations.AsNoTracking().SingleAsync(grant => grant.RunId == runId);

    public static Task<int> AgentAttemptCountAsync(DevalCopilotDbContext dbContext, Guid runId) =>
        dbContext.Attempts.AsNoTracking().CountAsync(attempt => attempt.RunId == runId && attempt.Kind == AttemptKind.Agent);
}
