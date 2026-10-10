namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

public enum QualificationResult
{
    /// <summary>One real Proposal and one real Acceptance or Challenge replying to it, with every control satisfied.</summary>
    Qualified,

    /// <summary>The ledger shows an allowance already spent: nothing was started.</summary>
    SessionSpent,

    /// <summary>Stopped before any allowance was spent (readiness, targets or setup).</summary>
    Blocked,

    /// <summary>An allowance was spent and the session did not reach a valid qualified result.</summary>
    Failed,
}
