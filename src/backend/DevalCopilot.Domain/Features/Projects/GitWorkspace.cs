namespace DevalCopilot.Domain.Features.Projects;

/// <summary>
/// One tool-owned Git worktree prepared for a registered project — created only from a fresh,
/// non-dirty baseline at an exact resolved commit SHA, never reused across preparation
/// attempts. Identity and fingerprint fields are immutable after creation; only
/// <see cref="Status"/> transitions, and only through the guarded, evidence-driven transitions
/// below. See ADR-0008.
/// </summary>
public sealed class GitWorkspace
{
    private GitWorkspace()
    {
    }

    public static GitWorkspace Prepare(
        Guid id,
        Guid projectId,
        int workspaceNumber,
        string workspacePath,
        string branchName,
        string sourceCommitSha,
        string? sourceBranchName,
        DateTimeOffset createdAtUtc)
    {
        if (workspaceNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(workspaceNumber));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceCommitSha);

        return new GitWorkspace
        {
            Id = id,
            ProjectId = projectId,
            WorkspaceNumber = workspaceNumber,
            WorkspacePath = workspacePath,
            BranchName = branchName,
            SourceCommitSha = sourceCommitSha,
            SourceBranchName = sourceBranchName,
            Status = WorkspaceStatus.Preparing,
            CreatedAtUtc = createdAtUtc,
        };
    }

    public Guid Id { get; private set; }

    public Guid ProjectId { get; private set; }

    /// <summary>Per-project monotonic, reserved via <see cref="Project.ReserveWorkspaceNumber"/>
    /// — never reused, so a retired workspace's path/branch is never recreated identically.</summary>
    public int WorkspaceNumber { get; private set; }

    public string WorkspacePath { get; private set; } = string.Empty;

    public string BranchName { get; private set; } = string.Empty;

    public string SourceCommitSha { get; private set; } = string.Empty;

    /// <summary>Null when the workspace was created from a detached HEAD — there was no branch
    /// name to record.</summary>
    public string? SourceBranchName { get; private set; }

    public WorkspaceStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>A fixed, safe reason code — never an exception message, OS error, or path —
    /// set whenever <see cref="Status"/> moves to a terminal or attention-needing state, so the
    /// UI can truthfully explain it. Null while <see cref="WorkspaceStatus.Preparing"/> or
    /// <see cref="WorkspaceStatus.Ready"/>.</summary>
    public string? LastFailureReasonCode { get; private set; }

    /// <summary>The in-request completion transition, once the durable ownership marker has
    /// itself been written and validated.</summary>
    public void MarkReady()
    {
        if (Status != WorkspaceStatus.Preparing)
        {
            throw new InvalidOperationException($"Cannot mark ready a workspace that is {Status}.");
        }

        Status = WorkspaceStatus.Ready;
    }

    /// <summary>The compensating transition for a proven Git-side-effect or marker-write
    /// failure — whether observed synchronously in the same request or found unrecoverable by
    /// startup reconciliation. Terminal: this workspace is never retried.</summary>
    public void MarkFailedToPrepare(string reasonCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);

        if (Status != WorkspaceStatus.Preparing)
        {
            throw new InvalidOperationException($"Cannot fail a workspace that is {Status}.");
        }

        Status = WorkspaceStatus.FailedToPrepare;
        LastFailureReasonCode = reasonCode;
    }

    /// <summary>Reconciliation found no worktree at the recorded path. Terminal.</summary>
    public void MarkMissingExternally(string reasonCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);

        if (Status is not (WorkspaceStatus.Ready or WorkspaceStatus.Preparing or WorkspaceStatus.NeedsAttention))
        {
            throw new InvalidOperationException($"Cannot mark missing a workspace that is {Status}.");
        }

        Status = WorkspaceStatus.MissingExternally;
        LastFailureReasonCode = reasonCode;
    }

    /// <summary>Reconciliation found the ownership marker missing, unparsable, or mismatched
    /// against the durable record. Terminal — never repaired or reused.</summary>
    public void MarkAlteredExternally(string reasonCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);

        if (Status is not (WorkspaceStatus.Ready or WorkspaceStatus.Preparing or WorkspaceStatus.NeedsAttention))
        {
            throw new InvalidOperationException($"Cannot mark altered a workspace that is {Status}.");
        }

        Status = WorkspaceStatus.AlteredExternally;
        LastFailureReasonCode = reasonCode;
    }

    /// <summary>The marker is valid (ownership stands) but the workspace's own HEAD no longer
    /// matches <see cref="SourceCommitSha"/> — something wrote into it from outside DevalCopilot.
    /// Not terminal: this remains the project's current workspace, visibly flagged.</summary>
    public void MarkNeedsAttention(string reasonCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);

        if (Status != WorkspaceStatus.Ready)
        {
            throw new InvalidOperationException($"Cannot flag needs-attention for a workspace that is {Status}.");
        }

        Status = WorkspaceStatus.NeedsAttention;
        LastFailureReasonCode = reasonCode;
    }
}
