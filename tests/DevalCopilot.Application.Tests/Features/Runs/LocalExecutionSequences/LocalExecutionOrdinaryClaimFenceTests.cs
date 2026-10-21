using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Projects.Commands.ClaimVerificationExecution;
using DevalCopilot.Application.Features.Runs.Commands.CreateChallengeResolutionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateClaudeCriticalReviewAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodeReviewAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodexPlanningAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateImplementationAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateVerificationDiagnosisAttempt;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;

/// <summary>
/// Ordinary (non-ticket) claims beside a local execution sequence (ADR-0060): unchanged while no sequence exists, withheld with the
/// stable ManualControlBlocked code at their early read while the fence holds (including format repairs), and withheld at the durable
/// seam, where the database trigger refuses a save of a claim that decided before the sequence was armed.
/// </summary>
public sealed class LocalExecutionOrdinaryClaimFenceTests : IAsyncLifetime
{
    public static IEnumerable<object[]> Entrances() => new[]
    {
        "implementation", "code_review", "verification_diagnosis", "codex_planning", "claude_critical_review", "challenge_resolution",
        "codex_planning_repair", "claude_critical_review_repair", "challenge_resolution_repair", "code_review_repair", "verification",
    }.Select(name => new object[] { name });

    public static IEnumerable<object[]> SeamEntrances() => new[]
    {
        "implementation", "codex_planning", "claude_critical_review", "challenge_resolution", "verification",
    }.Select(name => new object[] { name });

    private readonly SqliteDatabaseFixture fixture = new();
    private Guid challengedReviewId;
    private Guid unreviewedProposalId;

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    private async Task<(LocalExecutionSequenceTestScene Scene, LocalExecutionSequenceTestScene.SeededPlan Plan)> SceneAsync()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, recipeCount: 1);
        var plan = await scene.AddAcceptedRootPlanAsync();
        challengedReviewId = (await scene.AddChallengedPlanAsync()).ReviewAttemptId!.Value;
        unreviewedProposalId = await scene.AddNewerPlannerProposalAsync();
        return (scene, plan);
    }

    private async Task<Result> RunAsync(
        LocalExecutionSequenceTestScene scene, LocalExecutionSequenceTestScene.SeededPlan plan, string entrance, Func<FaultInjectingDbContext, FaultInjectingDbContext>? decorate = null)
    {
        FaultInjectingDbContext Context() => (decorate ?? (context => context))(new FaultInjectingDbContext(fixture.CreateContext()));
        var recipe = (await scene.ReadRecipesAsync())[0];
        var source = Guid.NewGuid();
        switch (entrance)
        {
            case "implementation":
                return Strip(await new CreateImplementationAttemptCommandHandler(Context(), scene.Evidence, scene.Artifacts, scene.Clock, scene.AttemptProbe)
                    .HandleAsync(new CreateImplementationAttemptCommand(scene.RunId, plan.ProposalId), CancellationToken.None));
            case "code_review":
                return Strip(await new CreateCodeReviewAttemptCommandHandler(Context(), scene.Evidence, scene.Artifacts, scene.Clock, scene.AttemptProbe)
                    .HandleAsync(new CreateCodeReviewAttemptCommand(scene.RunId, Guid.NewGuid()), CancellationToken.None));
            case "code_review_repair":
                return Strip(await new CreateCodeReviewAttemptCommandHandler(Context(), scene.Evidence, scene.Artifacts, scene.Clock, scene.AttemptProbe)
                    .HandleAsync(CreateCodeReviewAttemptCommand.ForRepair(scene.RunId, source), CancellationToken.None));
            case "verification_diagnosis":
                return Strip(await new CreateVerificationDiagnosisAttemptCommandHandler(Context(), scene.Evidence, scene.Artifacts, scene.Clock, scene.AttemptProbe)
                    .HandleAsync(new CreateVerificationDiagnosisAttemptCommand(scene.RunId, Guid.NewGuid()), CancellationToken.None));
            case "codex_planning":
                return Strip(await new CreateCodexPlanningAttemptCommandHandler(Context(), scene.Evidence, scene.Artifacts, scene.Clock, scene.AttemptProbe)
                    .HandleAsync(new CreateCodexPlanningAttemptCommand(scene.RunId), CancellationToken.None));
            case "codex_planning_repair":
                return Strip(await new CreateCodexPlanningAttemptCommandHandler(Context(), scene.Evidence, scene.Artifacts, scene.Clock, scene.AttemptProbe)
                    .HandleAsync(new CreateCodexPlanningAttemptCommand(scene.RunId, source), CancellationToken.None));
            case "claude_critical_review":
                return Strip(await new CreateClaudeCriticalReviewAttemptCommandHandler(Context(), scene.Evidence, scene.Artifacts, scene.Clock, scene.AttemptProbe)
                    .HandleAsync(new CreateClaudeCriticalReviewAttemptCommand(scene.RunId, unreviewedProposalId), CancellationToken.None));
            case "claude_critical_review_repair":
                return Strip(await new CreateClaudeCriticalReviewAttemptCommandHandler(Context(), scene.Evidence, scene.Artifacts, scene.Clock, scene.AttemptProbe)
                    .HandleAsync(CreateClaudeCriticalReviewAttemptCommand.ForRepair(scene.RunId, source), CancellationToken.None));
            case "challenge_resolution":
                return Strip(await new CreateChallengeResolutionAttemptCommandHandler(Context(), scene.Evidence, scene.Artifacts, scene.Clock, scene.AttemptProbe)
                    .HandleAsync(new CreateChallengeResolutionAttemptCommand(scene.RunId, challengedReviewId), CancellationToken.None));
            case "challenge_resolution_repair":
                return Strip(await new CreateChallengeResolutionAttemptCommandHandler(Context(), scene.Evidence, scene.Artifacts, scene.Clock, scene.AttemptProbe)
                    .HandleAsync(CreateChallengeResolutionAttemptCommand.ForRepair(scene.RunId, source), CancellationToken.None));
            default:
                return Strip(await new ClaimVerificationExecutionCommandHandler(Context(), scene.Evidence, scene.Clock)
                    .HandleAsync(new ClaimVerificationExecutionCommand(scene.ProjectId, recipe.Id, scene.CheckpointId), CancellationToken.None));
        }
    }

    private static Result Strip<T>(Result<T> result) => result.IsSuccess ? Result.Success() : Result.Failure(result.Errors);

    [Theory]
    [MemberData(nameof(Entrances))]
    public async Task While_the_fence_holds_an_ordinary_claim_is_refused_at_its_early_read_with_the_stable_code(string entrance)
    {
        var (scene, plan) = await SceneAsync();
        await scene.ArmAsync(plan.ProposalId);
        var before = await scene.FootprintAsync();

        var result = await RunAsync(scene, plan, entrance);

        Assert.True(result.IsFailure);
        Assert.Equal(LocalExecutionSequenceErrors.ManualControlBlockedCode, result.Errors[0].Code);
        Assert.Equal(before, await scene.FootprintAsync());
    }

    [Theory]
    [MemberData(nameof(SeamEntrances))]
    public async Task A_claim_that_decided_before_the_sequence_was_armed_is_refused_at_the_durable_seam_by_the_database_trigger(string entrance)
    {
        var (scene, plan) = await SceneAsync();
        var before = await scene.FootprintAsync();

        var result = await RunAsync(scene, plan, entrance, context =>
        {
            context.BeforeBeginTransaction = async _ => await scene.ArmAsync(plan.ProposalId);
            return context;
        });

        Assert.True(result.IsFailure, "the claim must not commit after the sequence was armed");
        Assert.Equal(LocalExecutionSequenceErrors.ManualControlBlockedCode, result.Errors[0].Code);
        var after = await scene.FootprintAsync();
        Assert.Equal(before.Attempts, after.Attempts);
        Assert.Equal(before.Executions, after.Executions);
        Assert.Equal(LocalExecutionSequenceState.Armed, (await scene.ReadSequenceAsync()).State);
    }

    [Fact]
    public async Task Without_a_sequence_an_ordinary_implementation_claim_is_unchanged()
    {
        var (scene, plan) = await SceneAsync();

        var result = await RunAsync(scene, plan, "implementation");

        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
        Assert.Null(await scene.FindSequenceAsync());
        Assert.Single(await scene.ReadAttemptsAsync(), attempt => attempt.AgentResponseContract == AgentResponseContract.ImplementationReport);
    }

    [Fact]
    public async Task Without_a_sequence_an_ordinary_verification_claim_is_unchanged()
    {
        var (scene, plan) = await SceneAsync();

        var result = await RunAsync(scene, plan, "verification");

        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
        Assert.Single(await scene.ReadExecutionsAsync());
    }

    [Fact]
    public async Task After_an_ended_sequence_with_no_owner_the_ordinary_claims_work_again()
    {
        var (scene, plan) = await SceneAsync();
        var armed = await scene.ArmAsync(plan.ProposalId);
        await scene.StopAsync(armed.SequenceId);

        var result = await RunAsync(scene, plan, "verification");

        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
    }
}
