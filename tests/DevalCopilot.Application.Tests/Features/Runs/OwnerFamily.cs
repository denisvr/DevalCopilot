namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>Which existing owner family a test places in the database (ADR-0058).</summary>
public enum OwnerFamily
{
    /// <summary>A Running, dispatched Implementation attempt (Domain classifies it WorkspaceMutating).</summary>
    Agent,

    /// <summary>A Running, dispatched verification execution.</summary>
    Verification,

    /// <summary>An open local-commit operation holding its Committing reservation.</summary>
    LocalCommit,
}
