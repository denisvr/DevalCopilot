namespace DevalCopilot.Api.Startup;

/// <summary>ADR-0072: the startup recovery step at which a fatal failure occurred, in execution order.</summary>
public enum StartupFailureStage
{
    StorageSchema = 0,
    LocalExecutionSequences = 1,
    ProcessAttemptOutput = 2,
    ProcessAttempts = 3,
    CapabilityCatalog = 4,
    CapabilityProbes = 5,
    LocalCommitOperations = 6,
    Workspaces = 7,
    VerificationOutput = 8,
    VerificationExecutions = 9,
    AgentAttemptOutput = 10,
    AgentAttempts = 11,
    ImplementationAttempts = 12,
    PlanningSequences = 13,
    AgentSupervisions = 14,
}
