namespace DevalCopilot.Application.Features.Runs.Policies;

/// <summary>Which of a bucket's two reported windows a warning fact came from. The provider's own labels are never kept.</summary>
public enum CodexAccountUsageWarningWindowKind
{
    Primary = 1,
    Secondary = 2,
}
