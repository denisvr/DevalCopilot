using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.RecordedLocalCommitInspection;

/// <summary>Seeds one ManualAgent run with the operation of the requested recorded shape through the Domain's own transitions over the
/// real migrated database of the host. It is for the recorded read only; the full approval lineage is exercised by the production host in
/// the local-commit endpoint tests.</summary>
internal static class LocalCommitInspectionApiSeed
{
    public static readonly DateTimeOffset Created = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset Started = Created.AddSeconds(5);
    public static readonly DateTimeOffset Finished = Created.AddSeconds(9);
    public static readonly string Parent = new('a', 40);
    public static readonly string Tree = new('e', 40);
    public static readonly string ProposedCommit = new('d', 40);
    public static readonly string Fingerprint = new('c', 64);

    public static async Task<LocalCommitInspectionApiScene> SeedAsync(
        DevalCopilotDbContext db, LocalCommitInspectionApiShape shape, string? reason = null)
    {
        var project = Project.Register(Guid.NewGuid(), "Local commit inspection", $@"C:\repos\{Guid.NewGuid():N}", Created);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, project.ReserveWorkspaceNumber(), $@"C:\workspaces\{Guid.NewGuid():N}",
            $"devalcopilot/workspace/{Guid.NewGuid():N}/1", Parent, "main", Created);
        workspace.MarkReady();
        var lease = RepositoryMutationLease.Acquire(
            Guid.NewGuid(), project.Id, workspace.Id, 1, Guid.NewGuid().ToByteArray(), Created);
        db.Projects.Add(project);
        db.GitWorkspaces.Add(workspace);
        db.RepositoryMutationLeases.Add(lease);
        await db.SaveChangesAsync();

        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), "Deliver", Created);
        run.Claim(Created);
        var checkpoint = GitCheckpoint.Capture(
            Guid.NewGuid(), workspace.Id, workspace.ReserveCheckpointNumber(), Created, Parent, Fingerprint, []);
        var attempt = Attempt.ClaimAgentCodeReview(
            Guid.NewGuid(), run.Id, 1, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(10), 1024, 2048,
            Created, 1);
        var agentReview = CheckpointReview.Record(
            Guid.NewGuid(), project.Id, workspace.Id, checkpoint.Id, checkpoint.CheckpointNumber, Fingerprint, ReviewActorKind.FutureAgent,
            ReviewDecision.Pending, Created, []);
        var humanReview = CheckpointReview.Record(
            Guid.NewGuid(), project.Id, workspace.Id, checkpoint.Id, checkpoint.CheckpointNumber, Fingerprint, ReviewActorKind.Human,
            ReviewDecision.Pending, Created, []);
        db.Runs.Add(run);
        db.GitCheckpoints.Add(checkpoint);
        db.Attempts.Add(attempt);
        db.CheckpointReviews.AddRange(agentReview, humanReview);
        await db.SaveChangesAsync();

        var operationId = Guid.NewGuid();
        var operation = LocalCommitOperation.Prepare(
            new LocalCommitOperation.PreparedFacts(
                operationId, run.Id, project.Id, workspace.Id, lease.Id, checkpoint.Id, checkpoint.CheckpointNumber, Fingerprint, attempt.Id,
                Guid.NewGuid(), Guid.NewGuid(), agentReview.Id, humanReview.Id, Fingerprint, Fingerprint, "Deliver", workspace.BranchName,
                Parent, Tree, ProposedCommit, "Local Owner", "owner@example.com", 1_800_000_000, Fingerprint, Fingerprint,
                $@"operations\{operationId:N}\prepared.index", 1, 10, Created),
            [LocalCommitAuthorityMember.Record(
                Guid.NewGuid(), operationId, LocalCommitAuthorityMemberKind.HumanReview, 0, humanReview.Id, null, Fingerprint)]);
        db.LocalCommitOperations.Add(operation);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE runs SET ExecutionMode = 2 WHERE Id = {run.Id}");

        var failure = reason ?? "local_commit.git_unprovable";
        switch (shape)
        {
            case LocalCommitInspectionApiShape.Executing:
                operation.MarkExecuting(Started);
                break;
            case LocalCommitInspectionApiShape.Completed:
                operation.MarkExecuting(Started);
                operation.Complete(Finished);
                break;
            case LocalCommitInspectionApiShape.FailedStarted:
                operation.MarkExecuting(Started);
                operation.Fail(failure, Finished);
                break;
            case LocalCommitInspectionApiShape.FailedUnstarted:
                operation.Fail(failure, Finished);
                break;
            case LocalCommitInspectionApiShape.InterruptedStarted:
                operation.MarkExecuting(Started);
                operation.Interrupt(failure, Finished);
                break;
            case LocalCommitInspectionApiShape.InterruptedUnstarted:
                operation.Interrupt(failure, Finished);
                break;
            case LocalCommitInspectionApiShape.AttentionStarted:
                operation.MarkExecuting(Started);
                operation.MarkNeedsAttention(failure);
                break;
            case LocalCommitInspectionApiShape.AttentionUnstarted:
                operation.MarkNeedsAttention(failure);
                break;
        }

        await db.SaveChangesAsync();
        return new LocalCommitInspectionApiScene(run.Id, project.Id, workspace.Id, checkpoint.Id, operationId, workspace.BranchName, ProposedCommit);
    }

    /// <summary>A registered run of an existing project that has no operation.</summary>
    public static async Task<Guid> SeedRunWithoutOperationAsync(DevalCopilotDbContext db, Guid projectId)
    {
        var number = await db.Runs.Where(run => run.ProjectId == projectId).CountAsync() + 1;
        var run = Run.RecordIntent(Guid.NewGuid(), projectId, number, "Later", Created.AddHours(1));
        db.Runs.Add(run);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE runs SET ExecutionMode = 2 WHERE Id = {run.Id}");
        return run.Id;
    }
}
