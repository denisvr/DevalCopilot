namespace DevalCopilot.Application.Features.Runs.Commands.CreateManualRun;

/// <summary>The accepted range of the owner's optional intake choices (ADR-0028). It is a transport rule for the creation of a
/// NEW manual run only: it never limits a historical or domain-created Run, whose persisted ceilings may exceed it.</summary>
public static class ManualRunBudgetRange
{
    public const int MinimumAgentAttempts = 1;

    public const int MaximumAgentAttempts = 16;

    public const int MinimumAgentInvocationMinutes = 1;

    public const int MaximumAgentInvocationMinutes = 120;
}
