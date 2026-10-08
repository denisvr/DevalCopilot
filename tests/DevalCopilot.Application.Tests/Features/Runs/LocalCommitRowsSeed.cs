using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>One Running run with its operation and every durable row the operation's foreign keys require.</summary>
internal sealed record LocalCommitRows(
    Project Project,
    Run Run,
    GitWorkspace Workspace,
    RepositoryMutationLease Lease,
    GitCheckpoint Checkpoint,
    LocalCommitOperation Operation);

/// <summary>Seeds the minimal relational rows that admit one <see cref="LocalCommitOperation"/> in a real SQLite database. It is for
/// recorder, recovery and reconciliation behavior only; the full approval lineage is exercised through the production host in the
/// API integration tests.</summary>
internal static class LocalCommitRowsSeed
{
    public static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
    public static readonly string SourceCommit = new('a', 40);
    private static readonly string Sha256 = new('c', 64);

    public static async Task<LocalCommitRows> SeedAsync(
        DevalCopilotDbContext db,
        LocalCommitStatus status = LocalCommitStatus.Executing,
        WorkspaceStatus workspaceStatus = WorkspaceStatus.Committing,
        string? parentCommit = null,
        string? commitSha = null)
    {
        var project = Project.Register(Guid.NewGuid(), "Local commit", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, project.ReserveWorkspaceNumber(), $@"C:\workspaces\{Guid.NewGuid():N}", "devalcopilot/workspace/x/1",
            SourceCommit, "main", Now);
        workspace.MarkReady();
        var lease = RepositoryMutationLease.Acquire(
            Guid.NewGuid(), project.Id, workspace.Id, 1, Guid.NewGuid().ToByteArray(), Now);
        db.Projects.Add(project);
        db.GitWorkspaces.Add(workspace);
        db.RepositoryMutationLeases.Add(lease);
        await db.SaveChangesAsync();

        var rows = await AddOperationAsync(db, project, workspace, lease, status, parentCommit ?? SourceCommit, commitSha ?? new string('d', 40));
        if (workspaceStatus == WorkspaceStatus.Committing)
        {
            await db.GitWorkspaces.Where(candidate => candidate.Id == workspace.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.Status, WorkspaceStatus.Committing));
        }
        else if (workspaceStatus != WorkspaceStatus.Ready)
        {
            await db.GitWorkspaces.Where(candidate => candidate.Id == workspace.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.Status, workspaceStatus));
        }

        return rows;
    }

    /// <summary>Adds another run and operation to the same workspace, as a later delivery in that workspace would.</summary>
    public static async Task<LocalCommitRows> AddOperationAsync(
        DevalCopilotDbContext db,
        Project project,
        GitWorkspace workspace,
        RepositoryMutationLease lease,
        LocalCommitStatus status,
        string parentCommit,
        string commitSha)
    {
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), "Deliver", Now);
        run.Claim(Now);
        var checkpoint = GitCheckpoint.Capture(
            Guid.NewGuid(), workspace.Id, workspace.ReserveCheckpointNumber(), Now, parentCommit, Sha256, []);
        var attempt = Attempt.ClaimAgentCodeReview(
            Guid.NewGuid(), run.Id, 1, Guid.NewGuid(), Guid.NewGuid(), Sha256, Guid.NewGuid(), TimeSpan.FromMinutes(10), 1024, 2048, Now, 1);
        var agentReview = CheckpointReview.Record(
            Guid.NewGuid(), project.Id, workspace.Id, checkpoint.Id, checkpoint.CheckpointNumber, Sha256, ReviewActorKind.FutureAgent,
            ReviewDecision.Pending, Now, []);
        var humanReview = CheckpointReview.Record(
            Guid.NewGuid(), project.Id, workspace.Id, checkpoint.Id, checkpoint.CheckpointNumber, Sha256, ReviewActorKind.Human,
            ReviewDecision.Pending, Now, []);
        db.Runs.Add(run);
        db.GitCheckpoints.Add(checkpoint);
        db.Attempts.Add(attempt);
        db.CheckpointReviews.AddRange(agentReview, humanReview);
        await db.SaveChangesAsync();

        var operationId = Guid.NewGuid();
        var operation = LocalCommitOperation.Prepare(
            new LocalCommitOperation.PreparedFacts(
                operationId, run.Id, project.Id, workspace.Id, lease.Id, checkpoint.Id, checkpoint.CheckpointNumber, Sha256, attempt.Id,
                Guid.NewGuid(), Guid.NewGuid(), agentReview.Id, humanReview.Id, Sha256, Sha256, "Deliver", workspace.BranchName,
                parentCommit, new string('e', 40), commitSha, "Local Owner", "owner@example.com", 1_800_000_000, Sha256, Sha256,
                $@"operations\{operationId:N}\prepared.index", 1, 10, Now),
            [LocalCommitAuthorityMember.Record(
                Guid.NewGuid(), operationId, LocalCommitAuthorityMemberKind.HumanReview, 0, humanReview.Id, null, Sha256)]);
        db.LocalCommitOperations.Add(operation);
        await db.SaveChangesAsync();

        if (status != LocalCommitStatus.Prepared)
        {
            operation.MarkExecuting(Now);
            if (status == LocalCommitStatus.NeedsAttention)
            {
                operation.MarkNeedsAttention("local_commit.seeded_attention");
            }
            else if (status == LocalCommitStatus.Completed)
            {
                operation.Complete(Now);
            }
            else if (status == LocalCommitStatus.Failed)
            {
                operation.Fail("local_commit.seeded_failure", Now);
            }
            else if (status == LocalCommitStatus.Interrupted)
            {
                operation.Interrupt("local_commit.seeded_interruption", Now);
            }

            await db.SaveChangesAsync();
        }

        return new LocalCommitRows(project, run, workspace, lease, checkpoint, operation);
    }
}
