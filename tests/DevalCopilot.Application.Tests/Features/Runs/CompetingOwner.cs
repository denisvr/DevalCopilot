namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>Which existing owner a test places beside the claim under judgment (ADR-0057).</summary>
public enum CompetingOwner
{
    /// <summary>A Running, dispatched verification execution of the same workspace.</summary>
    Verification,

    /// <summary>A Running Agent attempt of a different run of the same project on the same workspace.</summary>
    AgentOfAnotherRun,
}
