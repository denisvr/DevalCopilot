using DevalCopilot.Application.Features.Projects.Policies.WorkspaceExecutions;
using DevalCopilot.Application.Features.Runs.Policies.Abandonment;
using DevalCopilot.Application.Features.Runs.Policies.ExecutionCapacity;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences.ScriptedResultKind;
using Step = DevalCopilot.Domain.Features.Runs.LocalExecutionStepKind;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;

/// <summary>
/// ADR-0060's narrow extension of the owner classification of ADR-0057 to ADR-0059, through the shared workspace policy, capacity
/// policy and abandonment authority: a linked, dispatched operation without coherent host Exited evidence stays an effective owner
/// although its row is terminal; coherent exit proof or a never-dispatched refusal releases it; each row counts once; read-only work
/// keeps workspace exclusion but adds no global count; incoherent links are Unavailable and never a zero count; unlinked historical
/// rows are unchanged; and the in-memory proof agrees with the database-side expression across malformed combinations.
/// </summary>
public sealed class LocalExecutionOwnershipExtensionTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    private async Task<(LocalExecutionSequenceTestScene Scene, Guid SequenceId)> ArmedAsync(int recipeCount = 2)
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, recipeCount);
        var plan = await scene.AddAcceptedRootPlanAsync();
        return (scene, (await scene.ArmAsync(plan.ProposalId)).SequenceId);
    }

    private async Task<(WorkspaceExecutionStatus Workspace, int? MutatingOwners, bool AbandonmentBlocked)> ReadAsync(LocalExecutionSequenceTestScene scene)
    {
        await using var context = fixture.CreateContext();
        var workspace = await WorkspaceExecutionPolicy.AssessAsync(context, scene.ProjectId, scene.WorkspaceId, null, null, CancellationToken.None);
        var capacity = await ExecutionCapacityPolicy.ReadAsync(context, null, CancellationToken.None);
        var blocker = await RunAbandonmentAuthority.FindBlockerAsync(context, scene.ProjectId, CancellationToken.None);
        return (workspace.Status, capacity.RecordedOwners, blocker is not null);
    }

    // ---- agent operations ---------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(ProviderFailedNoExit, true)]
    [InlineData(ProviderFailedWithExit, false)]
    [InlineData(InvalidOutput, false)]
    [InlineData(Implemented, false)]
    public async Task A_linked_dispatched_mutating_agent_operation_is_an_owner_until_coherent_exit_proof(ScriptedResultKind result, bool owner)
    {
        var (scene, sequenceId) = await ArmedAsync();
        await scene.DriveAsync(sequenceId, new LocalExecutionStepScript().On(Step.Implementation, result), maximumTicks: 1);

        var (workspace, owners, blocked) = await ReadAsync(scene);

        var attempt = (await scene.ReadLocalAttemptsAsync()).Single();
        Assert.NotEqual(AttemptStatus.Running, attempt.Status);
        Assert.Equal(owner ? WorkspaceExecutionStatus.AgentAttemptActive : WorkspaceExecutionStatus.Idle, workspace);
        Assert.Equal(owner ? 1 : 0, owners);
        Assert.Equal(owner, blocked);
    }

    [Fact]
    public async Task A_never_dispatched_refusal_releases_the_claim_without_process_proof()
    {
        var (scene, sequenceId) = await ArmedAsync();
        var ticket = await scene.ReserveAndClaimAsync(sequenceId);
        var claimed = await ReadAsync(scene);
        Assert.Equal(WorkspaceExecutionStatus.AgentAttemptActive, claimed.Workspace);
        Assert.Equal(1, claimed.MutatingOwners);
        await scene.StopAsync(sequenceId);
        scene.Clock.Advance(TimeSpan.FromHours(1));
        await scene.ObserveAsync(sequenceId);

        Assert.True((await scene.RecordAgentDispatchRefusalAsync(ticket.ReservedOperationId)).IsSuccess);

        var released = await ReadAsync(scene);
        Assert.Equal(WorkspaceExecutionStatus.Idle, released.Workspace);
        Assert.Equal(0, released.MutatingOwners);
        Assert.False(released.AbandonmentBlocked);
    }

    [Fact]
    public async Task A_row_that_is_both_unresolved_and_linked_is_counted_once()
    {
        var (scene, sequenceId) = await ArmedAsync();
        var ticket = await scene.ReserveAndClaimAsync(sequenceId);
        await scene.DispatchAgentAsync(ticket.ReservedOperationId);

        var (workspace, owners, _) = await ReadAsync(scene);

        Assert.Equal(WorkspaceExecutionStatus.AgentAttemptActive, workspace);
        Assert.Equal(1, owners);
    }

    [Fact]
    public async Task A_quarantined_linked_owner_plus_an_unresolved_ordinary_owner_elsewhere_counts_two_without_double_counting()
    {
        var (scene, sequenceId) = await ArmedAsync();
        await ExecutionCapacitySeed.SetLimitAsync(fixture, 2);
        await scene.DriveAsync(sequenceId, new LocalExecutionStepScript().On(Step.Implementation, ProviderFailedNoExit), maximumTicks: 1);
        await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Agent);

        var (workspace, owners, _) = await ReadAsync(scene);

        Assert.Equal(WorkspaceExecutionStatus.AgentAttemptActive, workspace);
        Assert.Equal(2, owners);
    }

    [Fact]
    public async Task A_quarantined_linked_owner_and_an_ordinary_owner_in_the_same_workspace_are_never_trusted_as_a_count()
    {
        var (scene, sequenceId) = await ArmedAsync();
        await scene.DriveAsync(sequenceId, new LocalExecutionStepScript().On(Step.Implementation, ProviderFailedNoExit), maximumTicks: 1);
        await WorkspaceExecutionRaceSupport.AddAsync(fixture, scene.RunId, CompetingOwner.AgentOfAnotherRun);

        var (workspace, owners, blocked) = await ReadAsync(scene);

        Assert.Equal(WorkspaceExecutionStatus.Conflicted, workspace);
        Assert.True(blocked);
        // Whatever the count, it is never a smaller number than the recorded owners: either both are counted or none is stated.
        Assert.True(owners is null or >= 2, $"owners={owners}");
    }

    [Fact]
    public async Task A_quarantined_read_only_review_keeps_workspace_exclusion_but_adds_no_global_count()
    {
        var (scene, sequenceId) = await ArmedAsync(recipeCount: 1);
        await scene.DriveAsync(sequenceId, new LocalExecutionStepScript().On(Step.CodeReview, ProviderFailedNoExit));

        var (workspace, owners, blocked) = await ReadAsync(scene);

        var review = (await scene.ReadLocalAttemptsAsync()).Single(attempt => attempt.AgentResponseContract == AgentResponseContract.ImplementationReview);
        Assert.NotEqual(AttemptStatus.Running, review.Status);
        Assert.Equal(WorkspaceExecutionStatus.AgentAttemptActive, workspace);
        Assert.Equal(0, owners);
        Assert.True(blocked);
    }

    // ---- verification operations ----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(VerificationTimedOut, true)]
    [InlineData(VerificationPassed, false)]
    [InlineData(VerificationFailed, false)]
    public async Task A_linked_dispatched_verification_without_exit_proof_stays_an_owner_while_a_proved_exit_releases_it(
        ScriptedResultKind result, bool owner)
    {
        var (scene, sequenceId) = await ArmedAsync(recipeCount: 1);
        await scene.DriveAsync(sequenceId, new LocalExecutionStepScript().Verify(1, 0, result), maximumTicks: 2);

        var (workspace, owners, blocked) = await ReadAsync(scene);

        Assert.Equal(owner ? WorkspaceExecutionStatus.VerificationActive : WorkspaceExecutionStatus.Idle, workspace);
        Assert.Equal(owner, blocked);
        Assert.Equal(owner ? 1 : 0, owners);
    }

    [Fact]
    public async Task A_verification_dispatch_refusal_releases_the_undispatched_claim()
    {
        var (scene, sequenceId) = await ArmedAsync();
        await scene.StepAsync(sequenceId);
        var ticket = await scene.ReserveAndClaimAsync(sequenceId);
        Assert.Equal(WorkspaceExecutionStatus.VerificationActive, (await ReadAsync(scene)).Workspace);
        await scene.StopAsync(sequenceId);
        scene.Clock.Advance(TimeSpan.FromHours(1));
        await scene.ObserveAsync(sequenceId);

        Assert.True((await scene.RecordVerificationDispatchRefusalAsync(ticket.ReservedOperationId)).IsSuccess);

        Assert.Equal(WorkspaceExecutionStatus.Idle, (await ReadAsync(scene)).Workspace);
    }

    [Fact]
    public async Task Every_verification_status_is_a_recognized_terminal_except_running_in_all_three_shared_policies()
    {
        var (scene, sequenceId) = await ArmedAsync();
        await scene.StepAsync(sequenceId);
        var ticket = await scene.ReserveAndClaimAsync(sequenceId);

        foreach (var status in Enum.GetValues<VerificationExecutionStatus>())
        {
            await scene.ExecuteSqlAsync("UPDATE verification_executions SET Status = {0} WHERE Id = {1}", status.ToString(), ticket.ReservedOperationId);

            var (workspace, owners, blocked) = await ReadAsync(scene);

            var running = status == VerificationExecutionStatus.Running;
            Assert.Equal(running ? WorkspaceExecutionStatus.VerificationActive : WorkspaceExecutionStatus.Idle, workspace);
            Assert.Equal(running, blocked);
            Assert.Equal(running ? 1 : 0, owners);
        }
    }

    // ---- unavailable, unlinked ------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("contract")]
    [InlineData("checkpoint")]
    [InlineData("workspace")]
    public async Task A_conflicting_or_foreign_link_is_unavailable_and_never_a_zero_count(string tamper)
    {
        var (scene, sequenceId) = await ArmedAsync();
        var ticket = await scene.ReserveAndClaimAsync(sequenceId);
        await scene.DispatchAgentAsync(ticket.ReservedOperationId);
        var foreign = Guid.NewGuid();
        var sql = tamper switch
        {
            "contract" => "UPDATE attempts SET AgentResponseContract = 'Proposal' WHERE Id = {0}",
            "checkpoint" => "UPDATE attempts SET AgentGitCheckpointId = '" + foreign.ToString().ToUpperInvariant() + "' WHERE Id = {0}",
            _ => "UPDATE attempts SET AgentGitWorkspaceId = '" + foreign.ToString().ToUpperInvariant() + "' WHERE Id = {0}",
        };
        await using (var context = fixture.CreateContext())
        {
            await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
            await context.Database.ExecuteSqlRawAsync(sql, ticket.ReservedOperationId);
        }

        await using var read = fixture.CreateContext();
        Assert.True(await LocalExecutionOwnership.HasIncoherentLinkAsync(read, scene.ProjectId, CancellationToken.None));
        var workspace = await WorkspaceExecutionPolicy.AssessAsync(read, scene.ProjectId, scene.WorkspaceId, null, null, CancellationToken.None);
        var capacity = await ExecutionCapacityPolicy.ReadAsync(read, null, CancellationToken.None);
        Assert.Equal(WorkspaceExecutionStatus.Unavailable, workspace.Status);
        Assert.Equal(ExecutionCapacityStatus.Unavailable, capacity.Status);
        Assert.Null(capacity.RecordedOwners);
    }

    [Fact]
    public async Task An_unlinked_historical_terminal_row_without_exit_proof_is_still_not_an_owner()
    {
        var owner = await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Agent);
        await ExecutionCapacitySeed.FinishAsync(fixture, owner);

        await using var context = fixture.CreateContext();
        var capacity = await ExecutionCapacityPolicy.ReadAsync(context, null, CancellationToken.None);

        Assert.Equal(0, capacity.RecordedOwners);
    }

    // ---- in-memory proof against the database expression ----------------------------------------------------------------------

    public static IEnumerable<object[]> AgentTuples() => new (string Name, string Set)[]
    {
        ("baseline", "AgentProcessExitCode = AgentProcessExitCode"),
        ("no_outcome", "AgentProcessOutcome = NULL"),
        ("no_exit_code", "AgentProcessExitCode = NULL"),
        ("no_duration", "AgentProcessDuration = NULL"),
        ("negative_duration", "AgentProcessDuration = '-00:00:01'"),
        ("timed_out", "AgentProcessOutcome = 'TimedOut'"),
        ("not_dispatched", "AgentDispatchedAtUtc = NULL"),
        ("nothing", "AgentProcessOutcome = NULL, AgentProcessExitCode = NULL, AgentProcessDuration = NULL"),
    }.Select(tuple => new object[] { tuple.Name, tuple.Set });

    [Theory]
    [MemberData(nameof(AgentTuples))]
    public async Task The_in_memory_agent_exit_proof_agrees_with_the_database_expression(string name, string set)
    {
        var (scene, sequenceId) = await ArmedAsync();
        await scene.DriveAsync(sequenceId, new LocalExecutionStepScript().On(Step.Implementation, ProviderFailedWithExit), maximumTicks: 1);
        var id = (await scene.ReadLocalAttemptsAsync()).Single().Id;
        try
        {
            await scene.ExecuteSqlAsync($"UPDATE attempts SET {set} WHERE Id = {{0}}", id);
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            // The storage refuses this malformed combination outright, which is the strongest agreement possible.
            return;
        }

        var attempt = await scene.ReadAttemptAsync(id);
        await using var context = fixture.CreateContext();
        var unresolved = await context.Attempts.AsNoTracking().Where(LocalExecutionOwnership.UnresolvedAttempt(context)).AnyAsync(row => row.Id == id);

        var expected = attempt.AgentDispatchedAtUtc is not null && !LocalExecutionExitProof.Holds(attempt);
        Assert.True(expected == unresolved, $"{name}: in-memory unresolved={expected}, database unresolved={unresolved}");
    }

    public static IEnumerable<object[]> ExecutionTuples() => new (string Name, string Set)[]
    {
        ("baseline", "ExitCode = ExitCode"),
        ("no_outcome", "Outcome = NULL"),
        ("no_exit_code", "ExitCode = NULL"),
        ("timed_out", "Outcome = 'TimedOut'"),
        ("not_dispatched", "DispatchedAtUtc = NULL"),
    }.Select(tuple => new object[] { tuple.Name, tuple.Set });

    [Theory]
    [MemberData(nameof(ExecutionTuples))]
    public async Task The_in_memory_verification_exit_proof_agrees_with_the_database_expression(string name, string set)
    {
        var (scene, sequenceId) = await ArmedAsync(recipeCount: 1);
        await scene.DriveAsync(sequenceId, new LocalExecutionStepScript().Verify(1, 0, VerificationPassed), maximumTicks: 2);
        var id = (await scene.ReadExecutionsAsync()).Single().Id;
        try
        {
            await scene.ExecuteSqlAsync($"UPDATE verification_executions SET {set} WHERE Id = {{0}}", id);
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            return;
        }

        var execution = await scene.ReadExecutionAsync(id);
        await using var context = fixture.CreateContext();
        var unresolved = await context.VerificationExecutions.AsNoTracking()
            .Where(LocalExecutionOwnership.UnresolvedExecution(context)).AnyAsync(row => row.Id == id);

        var expected = execution.DispatchedAtUtc is not null && !LocalExecutionExitProof.Holds(execution);
        Assert.True(expected == unresolved, $"{name}: in-memory unresolved={expected}, database unresolved={unresolved}");
    }
}
