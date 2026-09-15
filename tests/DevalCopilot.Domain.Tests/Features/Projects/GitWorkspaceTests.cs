using DevalCopilot.Domain.Features.Projects;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Projects;

public sealed class GitWorkspaceTests
{
    private static GitWorkspace CreatePreparing() => GitWorkspace.Prepare(
        Guid.NewGuid(), Guid.NewGuid(), 1, @"C:\workspaces\p\1", "devalcopilot/workspace/p/1",
        "a".PadLeft(40, 'a'), "main", DateTimeOffset.UtcNow);

    [Fact]
    public void Prepare_starts_in_the_preparing_state_with_no_failure_reason()
    {
        var workspace = CreatePreparing();

        Assert.Equal(WorkspaceStatus.Preparing, workspace.Status);
        Assert.Null(workspace.LastFailureReasonCode);
    }

    [Fact]
    public void ReserveCheckpointNumber_is_monotonic_and_starts_at_one()
    {
        var workspace = CreatePreparing();

        Assert.Equal(1, workspace.ReserveCheckpointNumber());
        Assert.Equal(2, workspace.ReserveCheckpointNumber());
    }

    [Fact]
    public void Prepare_rejects_a_workspace_number_below_one()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GitWorkspace.Prepare(
            Guid.NewGuid(), Guid.NewGuid(), 0, @"C:\workspaces\p\1", "branch", "a".PadLeft(40, 'a'), null, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void MarkReady_transitions_from_preparing_only()
    {
        var workspace = CreatePreparing();

        workspace.MarkReady();

        Assert.Equal(WorkspaceStatus.Ready, workspace.Status);
        Assert.Throws<InvalidOperationException>(() => workspace.MarkReady());
    }

    [Fact]
    public void MarkFailedToPrepare_is_terminal_and_records_the_reason()
    {
        var workspace = CreatePreparing();

        workspace.MarkFailedToPrepare("workspaces.git_invocation_failed");

        Assert.Equal(WorkspaceStatus.FailedToPrepare, workspace.Status);
        Assert.Equal("workspaces.git_invocation_failed", workspace.LastFailureReasonCode);
        Assert.Throws<InvalidOperationException>(() => workspace.MarkFailedToPrepare("anything"));
    }

    [Fact]
    public void MarkMissingExternally_is_allowed_from_ready_and_records_the_reason()
    {
        var workspace = CreatePreparing();
        workspace.MarkReady();

        workspace.MarkMissingExternally("workspaces.reconciliation_worktree_missing");

        Assert.Equal(WorkspaceStatus.MissingExternally, workspace.Status);
        Assert.Equal("workspaces.reconciliation_worktree_missing", workspace.LastFailureReasonCode);
    }

    [Fact]
    public void MarkAlteredExternally_is_allowed_from_ready_and_is_terminal()
    {
        var workspace = CreatePreparing();
        workspace.MarkReady();

        workspace.MarkAlteredExternally("workspaces.reconciliation_marker_invalid");

        Assert.Equal(WorkspaceStatus.AlteredExternally, workspace.Status);
        Assert.Throws<InvalidOperationException>(() => workspace.MarkAlteredExternally("anything"));
    }

    [Fact]
    public void MarkNeedsAttention_is_allowed_only_from_ready_and_is_not_terminal()
    {
        var workspace = CreatePreparing();
        workspace.MarkReady();

        workspace.MarkNeedsAttention("workspaces.reconciliation_head_diverged");

        Assert.Equal(WorkspaceStatus.NeedsAttention, workspace.Status);

        // Not terminal in the domain-guard sense that other states are, but this slice never
        // attempts to promote it back to Ready, nor to flag it needs-attention twice.
        Assert.Throws<InvalidOperationException>(() => workspace.MarkNeedsAttention("anything"));
    }

    [Fact]
    public void MarkNeedsAttention_rejects_a_workspace_that_never_reached_ready()
    {
        var workspace = CreatePreparing();

        Assert.Throws<InvalidOperationException>(() => workspace.MarkNeedsAttention("workspaces.reconciliation_head_diverged"));
    }
}
