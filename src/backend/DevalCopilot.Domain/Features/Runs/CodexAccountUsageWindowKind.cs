namespace DevalCopilot.Domain.Features.Runs;

/// <summary>Which of a bucket's two reported usage windows a fact came from. The provider's own labels are never kept.</summary>
public enum CodexAccountUsageWindowKind
{
    Primary = 1,
    Secondary = 2,
}
