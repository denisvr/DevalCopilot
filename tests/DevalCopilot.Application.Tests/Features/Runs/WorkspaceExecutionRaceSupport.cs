using DevalCopilot.Application.Features.Projects.Policies.WorkspaceExecutions;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>Plants a competing workspace owner into a database an Agent claim scenario already seeded, and proves a refused claim left
/// nothing behind: no attempt, no input, no verification evidence, no artifact row, and no consumed claim number or budget slot.</summary>
internal static class WorkspaceExecutionRaceSupport
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    public static string CodeOf(CompetingOwner owner) => owner switch
    {
        CompetingOwner.Verification => WorkspaceExecutionErrors.VerificationActiveCode,
        _ => WorkspaceExecutionErrors.AgentAttemptActiveCode,
    };

    /// <summary>Places the owner on the latest workspace of the run's project, through its own connection, as a competing request would.</summary>
    public static async Task AddAsync(SqliteDatabaseFixture fixture, Guid runId, CompetingOwner owner, Guid? identifier = null)
    {
        await using var db = fixture.CreateContext();
        var run = await db.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == runId);
        var project = await db.Projects.SingleAsync(candidate => candidate.Id == run.ProjectId);
        var workspace = await db.GitWorkspaces.AsNoTracking().Where(candidate => candidate.ProjectId == project.Id)
            .OrderByDescending(candidate => candidate.WorkspaceNumber).FirstAsync();
        var checkpoint = await db.GitCheckpoints.AsNoTracking().Where(candidate => candidate.WorkspaceId == workspace.Id)
            .OrderByDescending(candidate => candidate.CheckpointNumber).FirstAsync();

        if (owner == CompetingOwner.Verification)
        {
            // Scenario seeds number their own rows, so the next numbers are read from the data, never from the project's counters.
            var commandNumber = 1 + (await db.VerificationCommands.AsNoTracking().Where(candidate => candidate.ProjectId == project.Id)
                .Select(candidate => (int?)candidate.CommandNumber).MaxAsync() ?? 0);
            var recipe = VerificationCommand.Configure(
                Guid.NewGuid(), project.Id, commandNumber, "Competing", @"C:\dotnet.exe", ["test"], 60, true, Now);
            db.VerificationCommands.Add(recipe);
            await db.SaveChangesAsync();
            var number = 1 + (await db.VerificationExecutions.AsNoTracking().Where(candidate => candidate.ProjectId == project.Id)
                .Select(candidate => (int?)candidate.ExecutionNumber).MaxAsync() ?? 0);
            var execution = VerificationExecution.Claim(identifier ?? Guid.NewGuid(), project.Id, number, workspace, checkpoint, recipe, Now);
            db.VerificationExecutions.Add(execution);
            await db.SaveChangesAsync();
            await db.VerificationExecutions.Where(candidate => candidate.Id == execution.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(candidate => candidate.DispatchedAtUtc, Now));
            return;
        }

        var runNumber = 1 + (await db.Runs.AsNoTracking().Where(candidate => candidate.ProjectId == project.Id)
            .Select(candidate => (int?)candidate.ExecutionNumber).MaxAsync() ?? 0);
        var other = Run.RecordIntent(Guid.NewGuid(), project.Id, runNumber, "Another run", Now);
        other.Claim(Now);
        db.Runs.Add(other);
        await db.SaveChangesAsync();
        var attempt = Attempt.ClaimAgentImplementation(
            identifier ?? Guid.NewGuid(), other.Id, 1, workspace.Id, checkpoint.Id, checkpoint.FingerprintSha256, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 1024, 2048, Now, 1);
        attempt.MarkAgentDispatched(Now);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync();
    }

    /// <summary>Places a verification execution of the run's project that names a workspace of ANOTHER project (a stored association no
    /// project can attest), through its own connection. The policy must read the project as Unavailable.</summary>
    public static async Task AddMisassociatedVerificationAsync(SqliteDatabaseFixture fixture, Guid runId)
    {
        var executionId = Guid.NewGuid();
        await AddAsync(fixture, runId, CompetingOwner.Verification, executionId);
        await using var db = fixture.CreateContext();
        var foreign = await Projects.WorkspaceExecutionSeed.SeedAsync(db);
        await db.VerificationExecutions.Where(candidate => candidate.Id == executionId)
            .ExecuteUpdateAsync(set => set.SetProperty(candidate => candidate.GitWorkspaceId, foreign.Workspace.Id));
    }

    /// <summary>The claim's own durable footprint on its run, taken before the claim: rows of this run only, so a competing owner planted
    /// on another run or in another table never disturbs the comparison.</summary>
    public sealed record Footprint(
        int Attempts, int InputMessages, int VerificationEvidence, int Artifacts, int Events, IReadOnlyList<int?> AgentBudgetSlots);

    public static async Task<Footprint> TakeAsync(SqliteDatabaseFixture fixture, Guid runId)
    {
        await using var db = fixture.CreateContext();
        var attemptIds = db.Attempts.AsNoTracking().Where(candidate => candidate.RunId == runId).Select(candidate => candidate.Id);
        return new Footprint(
            await db.Attempts.CountAsync(candidate => candidate.RunId == runId),
            await db.AttemptInputMessages.CountAsync(candidate => attemptIds.Contains(candidate.AttemptId)),
            await db.AttemptVerificationEvidence.CountAsync(candidate => attemptIds.Contains(candidate.AttemptId)),
            await db.Artifacts.CountAsync(candidate => candidate.RunId == runId),
            await db.Events.CountAsync(candidate => candidate.RunId == runId),
            (await db.Attempts.AsNoTracking().Where(candidate => candidate.RunId == runId && candidate.Kind == AttemptKind.Agent)
                .Select(candidate => candidate.AgentBudgetSlot).ToListAsync()).OrderBy(slot => slot).ToList());
    }

    /// <summary>A refused claim recorded no attempt, input, evidence, artifact or event of its own and consumed no Agent budget slot or
    /// claim number: the run's footprint equals what it was before the claim.</summary>
    public static async Task AssertUnchangedAsync(SqliteDatabaseFixture fixture, Guid runId, Footprint before)
    {
        var after = await TakeAsync(fixture, runId);
        Assert.Equal(before with { AgentBudgetSlots = [] }, after with { AgentBudgetSlots = [] });
        Assert.Equal(before.AgentBudgetSlots, after.AgentBudgetSlots);
    }
}
