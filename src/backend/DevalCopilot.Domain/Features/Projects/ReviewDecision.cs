namespace DevalCopilot.Domain.Features.Projects;

/// <summary>The immutable decision recorded by one checkpoint-bound review fact.</summary>
public enum ReviewDecision
{
    Pending = 0,
    Approved = 1,
    ChangesRequested = 2,
    Escalated = 3,
}
