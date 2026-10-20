using DevalCopilot.Application.Features.Runs.Policies.ExecutionCapacity;
using DevalCopilot.Application.Tests.Features.Projects;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// ADR-0058's one shared reading of how many potentially mutating owners are recorded across a database, against real file-backed SQLite
/// through independent connections: the fixed limit of two, every owner family and combination, recognized terminal history, recognized
/// ReadOnly Agent contracts and non-Agent attempts consuming nothing, an unknown possibly active status never being finished, and every
/// untrustworthy fact (unknown contract-role pair, missing or foreign association, orphan or malformed reservation, two owners on one
/// workspace) making the reading Unavailable instead of a convenient count. A run that ended, a changed lease or a workspace that is no
/// longer Ready never drops an unresolved owner, and a dispatch gate's own owner is validated in its own table before it is excluded.
/// </summary>
public sealed class ExecutionCapacityPolicyTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = WorkspaceExecutionSeed.Now;
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    private async Task<ExecutionCapacityReading> ReadAsync(ExecutionOwner? own = null)
    {
        await using var db = fixture.CreateContext();
        return await ExecutionCapacityPolicy.ReadAsync(db, own, CancellationToken.None);
    }

    private async Task ExecuteAsync(FormattableString sql)
    {
        await using var db = fixture.CreateContext();
        await db.Database.ExecuteSqlInterpolatedAsync(sql);
    }

    private static ExecutionOwnerKind KindOf(OwnerFamily family) => family switch
    {
        OwnerFamily.Agent => ExecutionOwnerKind.AgentAttempt,
        OwnerFamily.Verification => ExecutionOwnerKind.Verification,
        _ => ExecutionOwnerKind.LocalCommit,
    };

    private static void AssertReading(ExecutionCapacityReading reading, ExecutionCapacityStatus status, int? owners)
    {
        Assert.Equal(status, reading.Status);
        Assert.Equal(owners, reading.RecordedOwners);
    }

    [Fact]
    public async Task An_empty_database_has_a_limit_of_two_and_no_owner()
    {
        AssertReading(await ReadAsync(), ExecutionCapacityStatus.Available, 0);
        Assert.Equal(2, ExecutionCapacityPolicy.Limit);
    }

    [Theory]
    [InlineData(OwnerFamily.Agent)]
    [InlineData(OwnerFamily.Verification)]
    [InlineData(OwnerFamily.LocalCommit)]
    public async Task One_owner_of_each_family_is_one_place_and_leaves_one_available(OwnerFamily family)
    {
        await ExecutionCapacitySeed.AddAsync(fixture, family);

        AssertReading(await ReadAsync(), ExecutionCapacityStatus.Available, 1);
    }

    [Theory]
    [InlineData(OwnerFamily.Agent, OwnerFamily.Agent)]
    [InlineData(OwnerFamily.Agent, OwnerFamily.Verification)]
    [InlineData(OwnerFamily.Agent, OwnerFamily.LocalCommit)]
    [InlineData(OwnerFamily.Verification, OwnerFamily.Verification)]
    [InlineData(OwnerFamily.Verification, OwnerFamily.LocalCommit)]
    [InlineData(OwnerFamily.LocalCommit, OwnerFamily.LocalCommit)]
    public async Task Two_owners_of_any_families_on_independent_workspaces_are_at_capacity(OwnerFamily first, OwnerFamily second)
    {
        await ExecutionCapacitySeed.AddAsync(fixture, first, second);

        AssertReading(await ReadAsync(), ExecutionCapacityStatus.AtCapacity, 2);
    }

    [Fact]
    public async Task Old_over_capacity_facts_are_counted_exactly_and_rewritten_by_nothing()
    {
        await ExecutionCapacitySeed.AddAsync(
            fixture, OwnerFamily.Agent, OwnerFamily.Verification, OwnerFamily.LocalCommit, OwnerFamily.Verification);
        await using var before = fixture.CreateContext();
        var snapshot = await before.Attempts.CountAsync() * 1000 + await before.VerificationExecutions.CountAsync() * 100
            + await before.LocalCommitOperations.CountAsync();

        AssertReading(await ReadAsync(), ExecutionCapacityStatus.AtCapacity, 4);

        await using var after = fixture.CreateContext();
        Assert.Equal(
            snapshot,
            await after.Attempts.CountAsync() * 1000 + await after.VerificationExecutions.CountAsync() * 100
            + await after.LocalCommitOperations.CountAsync());
    }

    [Theory]
    [InlineData("planning")]
    [InlineData("critical-review")]
    [InlineData("challenge-resolution")]
    [InlineData("code-review")]
    public async Task Recognized_ReadOnly_Agent_attempts_consume_no_place_whatever_their_number(string path)
    {
        await AgentAttemptModeSeed.SeedClaimedAsync(fixture, path, AbandonmentRaceSupport.ManualAgentMode);
        await AgentAttemptModeSeed.SeedClaimedAsync(fixture, path, AbandonmentRaceSupport.ManualAgentMode);
        await AgentAttemptModeSeed.SeedClaimedAsync(fixture, path, AbandonmentRaceSupport.ManualAgentMode);

        AssertReading(await ReadAsync(), ExecutionCapacityStatus.Available, 0);
    }

    [Theory]
    [InlineData("implementation")]
    [InlineData("review-correction")]
    public async Task Each_WorkspaceMutating_Agent_contract_consumes_one_place(string path)
    {
        await AgentAttemptModeSeed.SeedClaimedAsync(fixture, path, AbandonmentRaceSupport.ManualAgentMode);

        AssertReading(await ReadAsync(), ExecutionCapacityStatus.Available, 1);
    }

    [Fact]
    public async Task The_classification_is_exactly_the_Domain_table_for_every_contract_and_ignores_the_provider()
    {
        foreach (var contract in Enum.GetValues<AgentResponseContract>())
        {
            var row = AgentAttemptContract.For(contract);
            Assert.Equal(row.Effect == AgentEffectKind.WorkspaceMutating, ExecutionCapacityPolicy.IsMutating(contract, row.Role));
            Assert.Equal(row.Effect == AgentEffectKind.ReadOnly, ExecutionCapacityPolicy.IsReadOnly(contract, row.Role));
            foreach (var other in Enum.GetValues<AgentRole>().Where(role => role != row.Role))
            {
                Assert.False(ExecutionCapacityPolicy.IsMutating(contract, other));
                Assert.False(ExecutionCapacityPolicy.IsReadOnly(contract, other));
            }
        }

        Assert.False(ExecutionCapacityPolicy.IsMutating(null, AgentRole.Implementer));
        Assert.False(ExecutionCapacityPolicy.IsReadOnly(AgentResponseContract.Proposal, null));
        Assert.False(ExecutionCapacityPolicy.IsMutating((AgentResponseContract)99, AgentRole.Implementer));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Simulated_and_standalone_Process_attempts_and_idle_prepared_workspaces_consume_nothing()
    {
        await using (var db = fixture.CreateContext())
        {
            var scene = await WorkspaceExecutionSeed.SeedAsync(db);
            var processRun = await WorkspaceExecutionSeed.AddRunAsync(db, scene.Project.Id, 2);
            db.Attempts.Add(Attempt.Claim(Guid.NewGuid(), scene.Run.Id, 1, Now));
            db.Attempts.Add(Attempt.ClaimProcess(
                Guid.NewGuid(), processRun.Id, 1, new ProcessExecutionIntent(@"C:\tool.exe", [], @"C:\work", @"C:\work", TimeSpan.FromMinutes(1), 1024, 2048), Now));
            await db.SaveChangesAsync();
            await WorkspaceExecutionSeed.SeedAsync(db);
        }

        AssertReading(await ReadAsync(), ExecutionCapacityStatus.Available, 0);
    }

    [Theory]
    [InlineData("Completed")]
    [InlineData("Failed")]
    [InlineData("Interrupted")]
    public async Task A_recognized_terminal_Agent_attempt_is_history(string status)
    {
        var owner = await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Agent);
        await ExecuteAsync($"UPDATE attempts SET Status = {status} WHERE Id = {owner.Id}");

        AssertReading(await ReadAsync(), ExecutionCapacityStatus.Available, 0);
    }

    [Theory]
    [InlineData("Passed")]
    [InlineData("Failed")]
    [InlineData("TimedOut")]
    [InlineData("Cancelled")]
    [InlineData("Interrupted")]
    [InlineData("SourceChanged")]
    public async Task A_recognized_terminal_verification_execution_is_history(string status)
    {
        var owner = await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Verification);
        await ExecuteAsync($"UPDATE verification_executions SET Status = {status} WHERE Id = {owner.Id}");

        AssertReading(await ReadAsync(), ExecutionCapacityStatus.Available, 0);
    }

    [Theory]
    [InlineData("Completed")]
    [InlineData("Failed")]
    [InlineData("Interrupted")]
    public async Task A_recognized_terminal_local_commit_operation_with_a_released_workspace_is_history(string status)
    {
        var owner = await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.LocalCommit);
        await ExecuteAsync($"UPDATE local_commit_operations SET Status = {status} WHERE Id = {owner.Id}");
        await ExecuteAsync($"UPDATE git_workspaces SET Status = {"Ready"} WHERE Id = {owner.WorkspaceId}");

        AssertReading(await ReadAsync(), ExecutionCapacityStatus.Available, 0);
    }

    [Theory]
    [InlineData(OwnerFamily.Agent)]
    [InlineData(OwnerFamily.Verification)]
    [InlineData(OwnerFamily.LocalCommit)]
    public async Task An_unrecognized_possibly_active_status_is_never_finished(OwnerFamily family)
    {
        var owner = await ExecutionCapacitySeed.AddAsync(fixture, family);
        switch (family)
        {
            case OwnerFamily.Agent:
                await ExecuteAsync($"UPDATE attempts SET Status = {"ZzUnrecognized"} WHERE Id = {owner.Id}");
                break;
            case OwnerFamily.Verification:
                await ExecuteAsync($"UPDATE verification_executions SET Status = {"ZzUnrecognized"} WHERE Id = {owner.Id}");
                break;
            default:
                await ExecuteAsync($"UPDATE local_commit_operations SET Status = {"ZzUnrecognized"} WHERE Id = {owner.Id}");
                break;
        }

        AssertReading(await ReadAsync(), ExecutionCapacityStatus.Available, 1);
    }

    [Theory]
    [InlineData("contract is null")]
    [InlineData("role is null")]
    [InlineData("contract is unrecognized")]
    [InlineData("role is unrecognized")]
    [InlineData("pair is incoherent")]
    public async Task An_active_Agent_attempt_without_a_coherent_contract_role_pair_is_Unavailable_never_implicitly_ReadOnly(string defect)
    {
        var owner = await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Agent);
        switch (defect)
        {
            case "contract is null":
                await ExecuteAsync($"UPDATE attempts SET AgentResponseContract = NULL WHERE Id = {owner.Id}");
                break;
            case "role is null":
                await ExecuteAsync($"UPDATE attempts SET AgentRole = NULL WHERE Id = {owner.Id}");
                break;
            case "contract is unrecognized":
                await ExecuteAsync($"UPDATE attempts SET AgentResponseContract = {"ZzUnrecognized"} WHERE Id = {owner.Id}");
                break;
            case "role is unrecognized":
                await ExecuteAsync($"UPDATE attempts SET AgentRole = {"ZzUnrecognized"} WHERE Id = {owner.Id}");
                break;
            default:
                await ExecuteAsync($"UPDATE attempts SET AgentRole = {"CodeReviewer"} WHERE Id = {owner.Id}");
                break;
        }

        AssertReading(await ReadAsync(), ExecutionCapacityStatus.Unavailable, null);
    }

    [Fact]
    public async Task An_unrecognized_attempt_kind_that_is_still_running_is_Unavailable()
    {
        var owner = await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Agent);
        await ExecuteAsync($"UPDATE attempts SET Kind = {"ZzUnrecognized"} WHERE Id = {owner.Id}");

        AssertReading(await ReadAsync(), ExecutionCapacityStatus.Unavailable, null);
    }

    [Fact]
    public async Task A_terminal_attempt_with_a_damaged_contract_is_history_and_never_makes_the_reading_Unavailable()
    {
        var owner = await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Agent);
        await ExecuteAsync($"UPDATE attempts SET AgentResponseContract = NULL, Status = {"Failed"} WHERE Id = {owner.Id}");

        AssertReading(await ReadAsync(), ExecutionCapacityStatus.Available, 0);
    }

    [Theory]
    [InlineData("a mutating attempt that names no workspace")]
    [InlineData("a mutating attempt whose workspace belongs to another project")]
    [InlineData("a verification execution whose workspace belongs to another project")]
    [InlineData("a verification execution that names a missing workspace")]
    [InlineData("a Committing workspace without an open operation")]
    [InlineData("an open operation whose workspace is Ready")]
    [InlineData("an open operation whose project differs from its workspace's")]
    [InlineData("an open operation whose run belongs to another project")]
    public async Task Ownership_that_cannot_be_attested_makes_the_reading_Unavailable(string defect)
    {
        switch (defect)
        {
            case "a mutating attempt that names no workspace":
                {
                    var owner = await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Agent);
                    await ExecuteAsync($"UPDATE attempts SET AgentGitWorkspaceId = NULL WHERE Id = {owner.Id}");
                    break;
                }

            case "a mutating attempt whose workspace belongs to another project":
                {
                    var owner = await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Agent);
                    var foreign = await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Verification);
                    await ExecuteAsync($"UPDATE attempts SET AgentGitWorkspaceId = {foreign.WorkspaceId} WHERE Id = {owner.Id}");
                    break;
                }

            case "a verification execution whose workspace belongs to another project":
                {
                    var owner = await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Verification);
                    var foreign = await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Agent);
                    await ExecuteAsync($"UPDATE verification_executions SET GitWorkspaceId = {foreign.WorkspaceId} WHERE Id = {owner.Id}");
                    break;
                }

            case "a verification execution that names a missing workspace":
                {
                    var owner = await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Verification);
                    await using var db = fixture.CreateContext();
                    await db.Database.OpenConnectionAsync();
                    await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");
                    await db.Database.ExecuteSqlInterpolatedAsync(
                        $"UPDATE verification_executions SET GitWorkspaceId = {Guid.NewGuid()} WHERE Id = {owner.Id}");
                    await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON");
                    break;
                }

            case "a Committing workspace without an open operation":
                {
                    var owner = await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.LocalCommit);
                    await ExecuteAsync($"UPDATE local_commit_operations SET Status = {"Completed"} WHERE Id = {owner.Id}");
                    break;
                }

            case "an open operation whose workspace is Ready":
                {
                    var owner = await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.LocalCommit);
                    await ExecuteAsync($"UPDATE git_workspaces SET Status = {"Ready"} WHERE Id = {owner.WorkspaceId}");
                    break;
                }

            case "an open operation whose project differs from its workspace's":
                {
                    var owner = await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.LocalCommit);
                    var foreign = await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Agent);
                    await ExecuteAsync($"UPDATE local_commit_operations SET ProjectId = {foreign.ProjectId} WHERE Id = {owner.Id}");
                    break;
                }

            default:
                {
                    var owner = await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.LocalCommit);
                    var foreign = await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Agent);
                    await using var db = fixture.CreateContext();
                    var foreignRunId = await db.Attempts.Where(attempt => attempt.Id == foreign.Id).Select(attempt => attempt.RunId).SingleAsync();
                    await ExecuteAsync($"UPDATE local_commit_operations SET RunId = {foreignRunId} WHERE Id = {owner.Id}");
                    break;
                }
        }

        AssertReading(await ReadAsync(), ExecutionCapacityStatus.Unavailable, null);
    }

    [Fact]
    public async Task A_NeedsAttention_workspace_with_its_open_operation_is_still_a_reservation_and_one_place()
    {
        var owner = await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.LocalCommit);
        await ExecuteAsync($"UPDATE git_workspaces SET Status = {"NeedsAttention"} WHERE Id = {owner.WorkspaceId}");
        await ExecuteAsync($"UPDATE local_commit_operations SET Status = {"NeedsAttention"} WHERE Id = {owner.Id}");

        AssertReading(await ReadAsync(), ExecutionCapacityStatus.Available, 1);
    }

    [Theory]
    [InlineData(OwnerFamily.Agent, OwnerFamily.Agent)]
    [InlineData(OwnerFamily.Agent, OwnerFamily.Verification)]
    [InlineData(OwnerFamily.Agent, OwnerFamily.LocalCommit)]
    [InlineData(OwnerFamily.Verification, OwnerFamily.Verification)]
    [InlineData(OwnerFamily.Verification, OwnerFamily.LocalCommit)]
    [InlineData(OwnerFamily.LocalCommit, OwnerFamily.Agent)]
    [InlineData(OwnerFamily.LocalCommit, OwnerFamily.Verification)]
    [InlineData(OwnerFamily.LocalCommit, OwnerFamily.LocalCommit)]
    public async Task Two_owners_recorded_on_one_workspace_are_Unavailable_not_one_coalesced_place(OwnerFamily first, OwnerFamily second)
    {
        var one = await ExecutionCapacitySeed.AddAsync(fixture, first);
        await ExecutionCapacitySeed.AddOntoAsync(fixture, one, second);

        AssertReading(await ReadAsync(), ExecutionCapacityStatus.Unavailable, null);
    }

    [Fact]
    public async Task An_owner_stays_counted_when_its_run_ended_its_lease_changed_or_its_workspace_is_no_longer_Ready()
    {
        var agent = await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Agent);
        var verification = await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Verification);
        await ExecuteAsync($"UPDATE runs SET Lifecycle = {"Completed"} WHERE ProjectId = {agent.ProjectId}");
        await ExecuteAsync($"UPDATE repository_mutation_leases SET Status = {"Released"} WHERE ProjectId = {verification.ProjectId}");
        await ExecuteAsync($"UPDATE git_workspaces SET Status = {"MissingExternally"} WHERE Id = {agent.WorkspaceId}");

        AssertReading(await ReadAsync(), ExecutionCapacityStatus.AtCapacity, 2);
    }

    [Fact]
    public async Task A_dispatch_gate_names_its_own_owner_and_the_reading_still_counts_it()
    {
        var own = await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Agent);
        await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Verification);

        AssertReading(
            await ReadAsync(new ExecutionOwner(ExecutionOwnerKind.AgentAttempt, own.Id)), ExecutionCapacityStatus.AtCapacity, 2);
    }

    [Theory]
    [InlineData(OwnerFamily.Agent, OwnerFamily.Verification)]
    [InlineData(OwnerFamily.Agent, OwnerFamily.LocalCommit)]
    [InlineData(OwnerFamily.Verification, OwnerFamily.Agent)]
    [InlineData(OwnerFamily.Verification, OwnerFamily.LocalCommit)]
    [InlineData(OwnerFamily.LocalCommit, OwnerFamily.Agent)]
    [InlineData(OwnerFamily.LocalCommit, OwnerFamily.Verification)]
    public async Task An_identifier_is_only_ever_validated_inside_its_own_table(OwnerFamily family, OwnerFamily wrongFamily)
    {
        var owner = await ExecutionCapacitySeed.AddAsync(fixture, family);

        AssertReading(await ReadAsync(new ExecutionOwner(KindOf(wrongFamily), owner.Id)), ExecutionCapacityStatus.Unavailable, null);
    }

    [Fact]
    public async Task A_dispatch_gate_whose_owner_is_not_a_coherent_counted_owner_is_Unavailable()
    {
        var owner = await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Agent);
        await ExecuteAsync($"UPDATE attempts SET Status = {"Completed"} WHERE Id = {owner.Id}");

        AssertReading(
            await ReadAsync(new ExecutionOwner(ExecutionOwnerKind.AgentAttempt, owner.Id)), ExecutionCapacityStatus.Unavailable, null);
        AssertReading(
            await ReadAsync(new ExecutionOwner(ExecutionOwnerKind.AgentAttempt, Guid.NewGuid())), ExecutionCapacityStatus.Unavailable, null);
    }

    [Fact]
    public async Task A_row_of_another_table_with_the_same_identifier_is_never_excluded_as_the_callers_own()
    {
        var agent = await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Agent);
        var verification = await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Verification);
        await ExecuteAsync($"UPDATE verification_executions SET Id = {agent.Id} WHERE Id = {verification.Id}");

        var reading = await ReadAsync(new ExecutionOwner(ExecutionOwnerKind.AgentAttempt, agent.Id));

        AssertReading(reading, ExecutionCapacityStatus.AtCapacity, 2);
    }

    [Fact]
    public async Task A_recorded_count_has_no_provider_or_observation_input()
    {
        var owner = await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Agent);
        await ExecuteAsync($"UPDATE attempts SET AgentProvider = {"Codex"} WHERE Id = {owner.Id}");

        AssertReading(await ReadAsync(), ExecutionCapacityStatus.Available, 1);
    }
}
