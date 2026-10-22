using DevalCopilot.Application.Features.Projects.Policies.WorkspaceExecutions;
using DevalCopilot.Application.Features.Runs.Policies.ExecutionCapacity;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;
using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionQueue;

/// <summary>LCQ-R1 (ADR-0062): a reservation is an owner, may be retired as unused, or may exclude itself only while its whole historical
/// identity (run, project, workspace, pinned and source checkpoint with fingerprint, complete admission/step tuple, closed kind mapping and
/// every ticket-carried identity) is proved from the database. A contradiction stays held, still owns, and is never released by Stop, a
/// preparation expiry or startup. No current eligibility, Git, provider or process is consulted.</summary>
public sealed class LocalExecutionQueueIdentityTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    private async Task<(LocalExecutionSequenceTestScene Scene, Guid SequenceId, LocalExecutionStepTicket Ticket)> ReservedAsync()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, 1);
        var plan = await scene.AddAcceptedRootPlanAsync();
        var sequenceId = (await scene.QueueAsync(plan.ProposalId)).SequenceId;
        var ticket = await scene.ReserveAsync();
        Assert.NotNull(ticket);
        return (scene, sequenceId, ticket);
    }

    private static LocalExecutionStepTicket With(
        LocalExecutionStepTicket ticket,
        Guid? ownerToken = null,
        Guid? reservedOperationId = null,
        Guid? sourceCheckpointId = null,
        Guid? reportMessageId = null,
        Guid? subjectAttemptId = null,
        Guid? recipeId = null,
        int? ordinal = null,
        LocalExecutionStepKind? kind = null) =>
        new(
            ticket.SequenceId, ticket.StepId, ordinal ?? ticket.Ordinal, kind ?? ticket.Kind, reservedOperationId ?? ticket.ReservedOperationId,
            ownerToken ?? ticket.OwnerToken, sourceCheckpointId ?? ticket.SourceCheckpointId, reportMessageId ?? ticket.ReportMessageId,
            subjectAttemptId ?? ticket.SubjectAttemptId, recipeId ?? ticket.RecipeId);

    private async Task ExecuteAsync(FormattableString statement)
    {
        await using var context = fixture.CreateContext();
        await context.Database.ExecuteSqlInterpolatedAsync(statement);
    }

    // ---- exact positive --------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_exact_ticket_of_a_coherent_reservation_excludes_only_its_own_reservation_and_no_other_ticket_does()
    {
        var (scene, _, ticket) = await ReservedAsync();

        Assert.NotEqual(WorkspaceExecutionStatus.Idle, (await scene.AssessWorkspaceAsync()).Status);
        Assert.Equal(WorkspaceExecutionStatus.Idle, (await scene.AssessWorkspaceAsync(ticket)).Status);
        Assert.Equal(0, (await scene.ReadCapacityAsync(ticket)).RecordedOwners);
        Assert.True((await scene.ReadCapacityAsync()).RecordedOwners > 0);
    }

    // ---- ticket-carried identities ---------------------------------------------------------------------------------------------------

    public static TheoryData<string> ForgedTickets() => new()
    {
        "owner-token", "reserved-operation", "source-checkpoint", "report-message", "subject-attempt", "recipe", "ordinal", "kind",
    };

    [Theory]
    [MemberData(nameof(ForgedTickets))]
    public async Task A_ticket_carrying_any_foreign_identity_cannot_exclude_the_reservation(string forgery)
    {
        var (scene, _, ticket) = await ReservedAsync();
        var forged = forgery switch
        {
            "owner-token" => With(ticket, ownerToken: Guid.NewGuid()),
            "reserved-operation" => With(ticket, reservedOperationId: Guid.NewGuid()),
            "source-checkpoint" => With(ticket, sourceCheckpointId: Guid.NewGuid()),
            "report-message" => With(ticket, reportMessageId: Guid.NewGuid()),
            "subject-attempt" => With(ticket, subjectAttemptId: Guid.NewGuid()),
            "recipe" => With(ticket, recipeId: Guid.NewGuid()),
            "ordinal" => With(ticket, ordinal: ticket.Ordinal + 1),
            _ => With(ticket, kind: LocalExecutionStepKind.CodeReview),
        };

        Assert.Equal(WorkspaceExecutionStatus.Unavailable, (await scene.AssessWorkspaceAsync(forged)).Status);
        Assert.Equal(ExecutionCapacityStatus.Unavailable, (await scene.ReadCapacityAsync(forged)).Status);
        // The reservation itself is untouched and still excludes with its exact ticket.
        Assert.Equal(WorkspaceExecutionStatus.Idle, (await scene.AssessWorkspaceAsync(ticket)).Status);
    }

    [Fact]
    public async Task A_ticket_of_an_unknown_sequence_or_step_is_invalid()
    {
        var (scene, _, ticket) = await ReservedAsync();
        var unknownSequence = new LocalExecutionStepTicket(
            Guid.NewGuid(), ticket.StepId, ticket.Ordinal, ticket.Kind, ticket.ReservedOperationId, ticket.OwnerToken,
            ticket.SourceCheckpointId, ticket.ReportMessageId, ticket.SubjectAttemptId, ticket.RecipeId);
        var unknownStep = new LocalExecutionStepTicket(
            ticket.SequenceId, Guid.NewGuid(), ticket.Ordinal, ticket.Kind, ticket.ReservedOperationId, ticket.OwnerToken,
            ticket.SourceCheckpointId, ticket.ReportMessageId, ticket.SubjectAttemptId, ticket.RecipeId);

        Assert.Equal(WorkspaceExecutionStatus.Unavailable, (await scene.AssessWorkspaceAsync(unknownSequence)).Status);
        Assert.Equal(WorkspaceExecutionStatus.Unavailable, (await scene.AssessWorkspaceAsync(unknownStep)).Status);
    }

    // ---- the closed recipe shape of a Verification ticket ----------------------------------------------------------------------------

    private async Task<(LocalExecutionSequenceTestScene Scene, LocalExecutionStepTicket Ticket)> ReservedVerificationAsync()
    {
        var (scene, sequenceId, implementation) = await ReservedAsync();
        await scene.DriveTicketAsync(implementation);
        await scene.ObserveAsync(sequenceId);
        var ticket = await scene.ReserveAsync();
        Assert.NotNull(ticket);
        Assert.Equal(LocalExecutionStepKind.Verification, ticket.Kind);
        return (scene, ticket);
    }

    [Fact]
    public async Task The_exact_verification_ticket_carries_its_pinned_recipe_and_excludes_only_its_own_reservation()
    {
        var (scene, ticket) = await ReservedVerificationAsync();

        Assert.NotNull(ticket.RecipeId);
        Assert.Equal(WorkspaceExecutionStatus.Idle, (await scene.AssessWorkspaceAsync(ticket)).Status);
        Assert.Equal(0, (await scene.ReadCapacityAsync(ticket)).RecordedOwners);
    }

    [Fact]
    public async Task A_verification_ticket_with_its_recipe_omitted_cannot_exclude_the_reservation()
    {
        var (scene, ticket) = await ReservedVerificationAsync();
        var omitted = new LocalExecutionStepTicket(
            ticket.SequenceId, ticket.StepId, ticket.Ordinal, ticket.Kind, ticket.ReservedOperationId, ticket.OwnerToken,
            ticket.SourceCheckpointId, ticket.ReportMessageId, ticket.SubjectAttemptId, recipeId: null);

        Assert.Equal(WorkspaceExecutionStatus.Unavailable, (await scene.AssessWorkspaceAsync(omitted)).Status);
        Assert.Equal(ExecutionCapacityStatus.Unavailable, (await scene.ReadCapacityAsync(omitted)).Status);
        Assert.Equal(WorkspaceExecutionStatus.Idle, (await scene.AssessWorkspaceAsync(ticket)).Status);
    }

    [Fact]
    public async Task A_verification_ticket_with_a_substituted_recipe_cannot_exclude_the_reservation()
    {
        var (scene, ticket) = await ReservedVerificationAsync();
        var substituted = With(ticket, recipeId: Guid.NewGuid());

        Assert.Equal(WorkspaceExecutionStatus.Unavailable, (await scene.AssessWorkspaceAsync(substituted)).Status);
        Assert.Equal(ExecutionCapacityStatus.Unavailable, (await scene.ReadCapacityAsync(substituted)).Status);
    }

    // ---- historical identity contradictions ------------------------------------------------------------------------------------------

    private async Task<Guid> TamperAsync(LocalExecutionSequenceTestScene scene, string tamper)
    {
        switch (tamper)
        {
            case "run-project":
                await using (var context = fixture.CreateContext())
                {
                    var other = Project.Register(Guid.NewGuid(), "Other identity fixture", $@"C:\identity-fixtures\{Guid.NewGuid():N}", scene.Clock.GetUtcNow());
                    context.Projects.Add(other);
                    await context.SaveChangesAsync();
                    Assert.Equal(1, await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE runs SET ProjectId = {other.Id} WHERE Id = {scene.RunId}"));
                    return other.Id;
                }

            case "pinned-fingerprint":
                await ExecuteAsync($"UPDATE git_checkpoints SET FingerprintSha256 = {new string('f', 64)} WHERE Id IN (SELECT CheckpointId FROM local_execution_sequences WHERE RunId = {scene.RunId})");
                return Guid.Empty;

            default:
                throw new ArgumentOutOfRangeException(nameof(tamper));
        }
    }

    public static TheoryData<string, string> Contradictions() => new()
    {
        { "run-project", "stop" }, { "run-project", "expiry" }, { "run-project", "startup" },
        { "pinned-fingerprint", "stop" }, { "pinned-fingerprint", "expiry" }, { "pinned-fingerprint", "startup" },
    };

    [Theory]
    [MemberData(nameof(Contradictions))]
    public async Task A_contradicted_historical_identity_is_never_retired_as_unused_and_keeps_owning(string tamper, string route)
    {
        var (scene, sequenceId, ticket) = await ReservedAsync();
        await TamperAsync(scene, tamper);

        if (route == "stop")
        {
            Assert.True((await scene.StopAsync(sequenceId)).IsSuccess);
        }
        else if (route == "expiry")
        {
            scene.Clock.Advance(TimeSpan.FromMinutes(3));
            await scene.ObserveAsync(sequenceId);
        }
        else
        {
            await scene.InterruptAsync();
        }

        var admission = Assert.Single(await scene.ReadAdmissionsAsync());
        Assert.Equal(LocalExecutionAdmissionState.Unresolved, admission.State);
        Assert.NotEqual(WorkspaceExecutionStatus.Idle, (await scene.AssessWorkspaceAsync()).Status);
        var capacity = await scene.ReadCapacityAsync();
        Assert.True(capacity.Status == ExecutionCapacityStatus.Unavailable || capacity.RecordedOwners > 0);
        // A contradicted identity can never excuse its own reservation either.
        Assert.NotEqual(WorkspaceExecutionStatus.Idle, (await scene.AssessWorkspaceAsync(ticket)).Status);
    }

    [Fact]
    public async Task A_coherent_reservation_is_still_retired_by_stop_with_its_token_fenced()
    {
        var (scene, sequenceId, _) = await ReservedAsync();

        Assert.True((await scene.StopAsync(sequenceId)).IsSuccess);

        Assert.Equal(LocalExecutionAdmissionState.Retired, Assert.Single(await scene.ReadAdmissionsAsync()).State);
        Assert.Equal(WorkspaceExecutionStatus.Idle, (await scene.AssessWorkspaceAsync()).Status);
    }

    [Theory]
    [InlineData("pinned-fingerprint")]
    public async Task The_shared_identity_predicate_agrees_with_the_ownership_counting(string tamper)
    {
        var (scene, _, _) = await ReservedAsync();
        await using (var before = fixture.CreateContext())
        {
            Assert.False(await LocalExecutionReservationOwnership.HasIncoherentReservationAsync(before, null, CancellationToken.None));
        }

        await TamperAsync(scene, tamper);

        await using var after = fixture.CreateContext();
        Assert.True(await LocalExecutionReservationOwnership.HasIncoherentReservationAsync(after, null, CancellationToken.None));
        var admission = await after.LocalExecutionAdmissions.AsNoTracking().SingleAsync();
        Assert.False(await LocalExecutionReservationOwnership.IdentityHoldsAsync(after, admission, CancellationToken.None));
    }

    [Theory]
    [InlineData(LocalExecutionStepKind.Implementation, true)]
    [InlineData(LocalExecutionStepKind.Verification, true)]
    [InlineData(LocalExecutionStepKind.VerificationDiagnosis, true)]
    [InlineData(LocalExecutionStepKind.DiagnosisCorrection, true)]
    [InlineData(LocalExecutionStepKind.ReviewCorrection, true)]
    [InlineData(LocalExecutionStepKind.CodeReview, true)]
    [InlineData((LocalExecutionStepKind)99, false)]
    public void The_closed_kind_mapping_accepts_every_defined_kind_and_no_other(LocalExecutionStepKind kind, bool expected) =>
        Assert.Equal(expected, LocalExecutionReservationOwnership.KindMappingHolds(kind));
}
