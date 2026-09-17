namespace DevalCopilot.Domain.Features.Runs;

/// <summary>How a collaboration fact entered the project-owned ledger.</summary>
public enum CollaborationMessageProvenance
{
    Simulated = 0,
    ProviderObserved = 1,
}
