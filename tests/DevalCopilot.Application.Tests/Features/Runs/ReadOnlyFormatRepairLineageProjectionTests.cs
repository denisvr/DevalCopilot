using DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptEvidence;
using DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptHistory;
using DevalCopilot.Application.Features.Runs.Queries.GetChallengeResolutionAttemptStatus;
using DevalCopilot.Application.Features.Runs.Queries.GetClaudeCriticalReviewAttemptStatus;
using DevalCopilot.Application.Features.Runs.Queries.GetCodeReviewAttemptStatus;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// The repair lineage the three role status contracts and the historical attempt evidence and history expose:
/// the source's id and number for a repair, nothing for an ordinary attempt, and — for the historical
/// projections only — a link that cannot be proved (a source of another run, or one that is not earlier)
/// reads as no lineage rather than a guessed one. Provenance only; nothing claims the source was fixed.
/// </summary>
public sealed class ReadOnlyFormatRepairLineageProjectionTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private sealed record Arranged(RepairTestScene Scene, Attempt Source, Attempt Repair);

    private async Task<Arranged> CriticalAsync()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (source, _) = scene.AddCriticalReviewSource();
        await scene.SaveAsync();
        return new Arranged(scene, source, await scene.AddRepairAsync(source));
    }

    private async Task<Arranged> ResolverAsync()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (source, _, _) = scene.AddResolverSource();
        await scene.SaveAsync();
        return new Arranged(scene, source, await scene.AddRepairAsync(source));
    }

    private async Task<Arranged> CodeReviewAsync()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var implementation = scene.AddInitialImplementation();
        var verification = await scene.AddPassedVerificationAsync(implementation);
        var source = scene.AddInvalidCodeReview(implementation, verification);
        await scene.SaveAsync();
        return new Arranged(scene, source, await scene.AddRepairAsync(source));
    }

    [Fact]
    public async Task The_critical_review_status_shows_the_repair_lineage_and_none_for_the_ordinary_source()
    {
        var arranged = await CriticalAsync();
        await using var context = _fixture.CreateContext();
        var handler = new GetClaudeCriticalReviewAttemptStatusQueryHandler(context);

        var status = (await handler.HandleAsync(new GetClaudeCriticalReviewAttemptStatusQuery(arranged.Scene.Run.Id), CancellationToken.None)).Value;

        Assert.Equal(arranged.Repair.Id, status.AttemptId);
        Assert.Equal(arranged.Source.Id, status.RepairSourceAttemptId);
        Assert.Equal(arranged.Source.AttemptNumber, status.RepairSourceAttemptNumber);

        await using var ordinaryContext = _fixture.CreateContext();
        await ordinaryContext.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM attempts WHERE Id = {arranged.Repair.Id}");
        var ordinary = (await new GetClaudeCriticalReviewAttemptStatusQueryHandler(ordinaryContext).HandleAsync(
            new GetClaudeCriticalReviewAttemptStatusQuery(arranged.Scene.Run.Id), CancellationToken.None)).Value;
        Assert.Equal(arranged.Source.Id, ordinary.AttemptId);
        Assert.Null(ordinary.RepairSourceAttemptId);
        Assert.Null(ordinary.RepairSourceAttemptNumber);
    }

    [Fact]
    public async Task The_resolution_status_shows_the_repair_lineage()
    {
        var arranged = await ResolverAsync();
        await using var context = _fixture.CreateContext();

        var status = (await new GetChallengeResolutionAttemptStatusQueryHandler(context).HandleAsync(
            new GetChallengeResolutionAttemptStatusQuery(arranged.Scene.Run.Id), CancellationToken.None)).Value;

        Assert.Equal(arranged.Repair.Id, status.AttemptId);
        Assert.Equal(arranged.Source.Id, status.RepairSourceAttemptId);
        Assert.Equal(arranged.Source.AttemptNumber, status.RepairSourceAttemptNumber);
    }

    [Fact]
    public async Task The_code_review_status_shows_the_repair_lineage()
    {
        var arranged = await CodeReviewAsync();
        await using var context = _fixture.CreateContext();

        var status = (await new GetCodeReviewAttemptStatusQueryHandler(context).HandleAsync(
            new GetCodeReviewAttemptStatusQuery(arranged.Scene.Run.Id), CancellationToken.None)).Value;

        Assert.Equal(arranged.Repair.Id, status.AttemptId);
        Assert.Equal(arranged.Source.Id, status.RepairSourceAttemptId);
        Assert.Equal(arranged.Source.AttemptNumber, status.RepairSourceAttemptNumber);
    }

    [Fact]
    public async Task A_role_status_keeps_the_link_id_but_no_number_when_no_source_of_the_run_is_found()
    {
        var arranged = await CriticalAsync();
        var other = await RepairTestScene.CreateAsync(_fixture);
        var (foreignSource, _) = other.AddCriticalReviewSource();
        await other.SaveAsync();
        await arranged.Scene.CorruptAsync(arranged.Repair.Id, $"AgentRepairSourceAttemptId = '{foreignSource.Id.ToString().ToUpperInvariant()}'");
        await using var context = _fixture.CreateContext();

        var status = (await new GetClaudeCriticalReviewAttemptStatusQueryHandler(context).HandleAsync(
            new GetClaudeCriticalReviewAttemptStatusQuery(arranged.Scene.Run.Id), CancellationToken.None)).Value;

        Assert.Equal(foreignSource.Id, status.RepairSourceAttemptId);
        Assert.Null(status.RepairSourceAttemptNumber);
    }

    [Theory]
    [InlineData("critical")]
    [InlineData("resolver")]
    [InlineData("codereview")]
    public async Task The_history_and_evidence_show_the_proved_lineage_for_every_role(string stage)
    {
        var arranged = stage switch { "critical" => await CriticalAsync(), "resolver" => await ResolverAsync(), _ => await CodeReviewAsync() };
        await using var context = _fixture.CreateContext();

        var history = (await new GetAgentAttemptHistoryQueryHandler(context).HandleAsync(
            new GetAgentAttemptHistoryQuery(arranged.Scene.Run.Id, null, 20), CancellationToken.None)).Value;
        var repairEntry = Assert.Single(history.Items, item => item.AttemptId == arranged.Repair.Id);
        Assert.Equal(arranged.Source.Id, repairEntry.RepairSourceAttemptId);
        Assert.Equal(arranged.Source.AttemptNumber, repairEntry.RepairSourceAttemptNumber);
        var sourceEntry = Assert.Single(history.Items, item => item.AttemptId == arranged.Source.Id);
        Assert.Null(sourceEntry.RepairSourceAttemptId);
        Assert.Null(sourceEntry.RepairSourceAttemptNumber);

        var evidence = (await new GetAgentAttemptEvidenceQueryHandler(context).HandleAsync(
            new GetAgentAttemptEvidenceQuery(arranged.Scene.Run.Id, arranged.Repair.Id), CancellationToken.None)).Value;
        Assert.Equal(arranged.Source.Id, evidence.RepairSourceAttemptId);
        Assert.Equal(arranged.Source.AttemptNumber, evidence.RepairSourceAttemptNumber);
    }

    [Fact]
    public async Task The_existing_planner_repair_link_is_now_also_shown_in_the_history_and_evidence()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var number = scene.Lineage.ReserveAttemptNumber();
        var source = Attempt.ClaimAgent(
            Guid.NewGuid(), scene.Run.Id, number, scene.Workspace.Id, scene.Checkpoint.Id, RepairTestScene.Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, RepairTestScene.Now, number);
        RepairTestScene.Invalidate(source, RepairTestScene.Fingerprint);
        scene.Db.Attempts.Add(source);
        var repairNumber = scene.Lineage.ReserveAttemptNumber();
        var repair = Attempt.ClaimAgentPlanningRepair(
            Guid.NewGuid(), scene.Run.Id, repairNumber, scene.Workspace.Id, scene.Checkpoint.Id, RepairTestScene.Fingerprint,
            Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, RepairTestScene.Now, null, null, repairNumber, source.Id);
        scene.Db.Attempts.Add(repair);
        await scene.SaveAsync();
        await using var context = _fixture.CreateContext();

        var evidence = (await new GetAgentAttemptEvidenceQueryHandler(context).HandleAsync(
            new GetAgentAttemptEvidenceQuery(scene.Run.Id, repair.Id), CancellationToken.None)).Value;

        Assert.Equal(source.Id, evidence.RepairSourceAttemptId);
        Assert.Equal(number, evidence.RepairSourceAttemptNumber);
    }

    [Fact]
    public async Task A_link_that_cannot_be_proved_reads_as_no_lineage_in_the_history_and_evidence()
    {
        var arranged = await CriticalAsync();
        var other = await RepairTestScene.CreateAsync(_fixture);
        var (foreignSource, _) = other.AddCriticalReviewSource();
        await other.SaveAsync();
        await arranged.Scene.CorruptAsync(arranged.Repair.Id, $"AgentRepairSourceAttemptId = '{foreignSource.Id.ToString().ToUpperInvariant()}'");
        await using var context = _fixture.CreateContext();

        var history = (await new GetAgentAttemptHistoryQueryHandler(context).HandleAsync(
            new GetAgentAttemptHistoryQuery(arranged.Scene.Run.Id, null, 20), CancellationToken.None)).Value;
        var entry = Assert.Single(history.Items, item => item.AttemptId == arranged.Repair.Id);
        Assert.Null(entry.RepairSourceAttemptId);
        Assert.Null(entry.RepairSourceAttemptNumber);

        var evidence = (await new GetAgentAttemptEvidenceQueryHandler(context).HandleAsync(
            new GetAgentAttemptEvidenceQuery(arranged.Scene.Run.Id, arranged.Repair.Id), CancellationToken.None)).Value;
        Assert.Null(evidence.RepairSourceAttemptId);
        Assert.Null(evidence.RepairSourceAttemptNumber);
    }

    [Fact]
    public async Task A_link_to_a_later_attempt_reads_as_no_lineage_and_an_unreadable_repair_row_discloses_none()
    {
        var arranged = await CriticalAsync();
        // The "source" is claimed after the repair: an impossible order, so it is not proved as a source.
        await arranged.Scene.CorruptAsync(arranged.Source.Id, "AttemptNumber = 90, AgentBudgetSlot = 90");
        await using var context = _fixture.CreateContext();

        var evidence = (await new GetAgentAttemptEvidenceQueryHandler(context).HandleAsync(
            new GetAgentAttemptEvidenceQuery(arranged.Scene.Run.Id, arranged.Repair.Id), CancellationToken.None)).Value;
        Assert.Null(evidence.RepairSourceAttemptId);
        Assert.Null(evidence.RepairSourceAttemptNumber);

        await arranged.Scene.CorruptAsync(arranged.Repair.Id, "AgentOutcome = 'NoSuchOutcome'");
        await using var unreadableContext = _fixture.CreateContext();
        var unreadable = (await new GetAgentAttemptEvidenceQueryHandler(unreadableContext).HandleAsync(
            new GetAgentAttemptEvidenceQuery(arranged.Scene.Run.Id, arranged.Repair.Id), CancellationToken.None)).Value;
        Assert.False(unreadable.IdentityValid);
        Assert.Null(unreadable.RepairSourceAttemptId);
        Assert.Null(unreadable.RepairSourceAttemptNumber);
    }
}
