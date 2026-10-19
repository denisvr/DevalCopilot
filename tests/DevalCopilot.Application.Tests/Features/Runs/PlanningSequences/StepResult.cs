namespace DevalCopilot.Application.Tests.Features.Runs.PlanningSequences;

/// <summary>The result of one claimed step as the durable records a provider result produces.</summary>
public enum StepResult
{
    Proposed,
    Accepted,
    Challenged,
    Resolved,
    InvalidOutput,
    ProviderFailed,
}
