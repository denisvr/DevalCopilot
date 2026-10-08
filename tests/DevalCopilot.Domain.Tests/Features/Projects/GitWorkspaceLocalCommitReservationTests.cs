using DevalCopilot.Domain.Features.Projects;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Projects;

/// <summary>The durable Committing reservation of ADR-0029 on the workspace aggregate: exactly one Ready workspace is reserved,
/// ambiguity keeps the reservation under attention, and only that same attention can be released by exact reconciliation.</summary>
public sealed class GitWorkspaceLocalCommitReservationTests
{
    private static GitWorkspace Ready()
    {
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), Guid.NewGuid(), 1, @"C:\workspaces\p\1", "devalcopilot/workspace/p/1", new string('a', 40), "main",
            DateTimeOffset.UtcNow);
        workspace.MarkReady();
        return workspace;
    }

    [Fact]
    public void The_existing_status_values_are_preserved_and_committing_is_added_last()
    {
        Assert.Equal(0, (int)WorkspaceStatus.Preparing);
        Assert.Equal(1, (int)WorkspaceStatus.Ready);
        Assert.Equal(2, (int)WorkspaceStatus.FailedToPrepare);
        Assert.Equal(3, (int)WorkspaceStatus.MissingExternally);
        Assert.Equal(4, (int)WorkspaceStatus.AlteredExternally);
        Assert.Equal(5, (int)WorkspaceStatus.NeedsAttention);
        Assert.Equal(6, (int)WorkspaceStatus.Committing);
    }

    [Fact]
    public void Only_a_ready_workspace_can_be_reserved()
    {
        var workspace = Ready();
        workspace.BeginCommit();
        Assert.Equal(WorkspaceStatus.Committing, workspace.Status);

        Assert.Throws<InvalidOperationException>(() => workspace.BeginCommit());
        var attention = Ready();
        attention.MarkNeedsAttention("workspaces.reconciliation_head_diverged");
        Assert.Throws<InvalidOperationException>(() => attention.BeginCommit());
        var preparing = GitWorkspace.Prepare(
            Guid.NewGuid(), Guid.NewGuid(), 1, @"C:\w", "branch", new string('a', 40), "main", DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(() => preparing.BeginCommit());
    }

    [Fact]
    public void A_reservation_ends_only_by_finishing_or_by_attention_and_finishing_clears_the_reason()
    {
        var workspace = Ready();
        Assert.Throws<InvalidOperationException>(() => workspace.FinishCommit());

        workspace.BeginCommit();
        workspace.FinishCommit();

        Assert.Equal(WorkspaceStatus.Ready, workspace.Status);
        Assert.Null(workspace.LastFailureReasonCode);
    }

    [Fact]
    public void Ambiguity_keeps_the_reservation_under_attention_that_only_local_commit_reconciliation_may_release()
    {
        var workspace = Ready();
        workspace.BeginCommit();
        workspace.MarkNeedsAttention(GitWorkspace.LocalCommitAttentionPrefix + "ambiguous");

        Assert.Equal(WorkspaceStatus.NeedsAttention, workspace.Status);
        Assert.True(workspace.IsAttentionFromLocalCommit);
        workspace.FinishCommit();
        Assert.Equal(WorkspaceStatus.Ready, workspace.Status);
        Assert.False(workspace.IsAttentionFromLocalCommit);
    }

    [Fact]
    public void A_foreign_needs_attention_is_never_released_by_the_local_commit_path()
    {
        var workspace = Ready();
        workspace.MarkNeedsAttention("workspaces.reconciliation_head_diverged");

        Assert.False(workspace.IsAttentionFromLocalCommit);
        Assert.Throws<InvalidOperationException>(() => workspace.FinishCommit());
        Assert.Equal(WorkspaceStatus.NeedsAttention, workspace.Status);
        Assert.Equal("workspaces.reconciliation_head_diverged", workspace.LastFailureReasonCode);
    }

    [Fact]
    public void Attention_may_be_flagged_from_ready_or_committing_but_from_no_other_status()
    {
        var committing = Ready();
        committing.BeginCommit();
        committing.MarkNeedsAttention(GitWorkspace.LocalCommitAttentionPrefix + "source_changed_after_commit");
        Assert.Equal(WorkspaceStatus.NeedsAttention, committing.Status);

        var missing = Ready();
        missing.MarkMissingExternally("workspaces.reconciliation_worktree_missing");
        Assert.Throws<InvalidOperationException>(() => missing.MarkNeedsAttention("local_commit.ambiguous"));
    }
}
