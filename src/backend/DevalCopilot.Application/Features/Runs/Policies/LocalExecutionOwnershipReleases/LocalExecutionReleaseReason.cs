namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionOwnershipReleases;

/// <summary>The closed, fixed reason vocabulary of a release reading that is neither Available nor Released (ADR-0065). Each value has one
/// fixed snake_case wire code and carries no stored value, identifier or detail.</summary>
internal enum LocalExecutionReleaseReason
{
    None = 0,

    /// <summary>The step names no operation: a reservation, an unresolved preparation or a revoked step. It owns no operation to release.</summary>
    StepNotLinked = 1,

    /// <summary>The linked operation is still Running: nothing about a still-running row is terminalized or released.</summary>
    OperationNotTerminal = 2,

    /// <summary>The linked operation was never dispatched, so it never owned its workspace.</summary>
    NotDispatched = 3,

    /// <summary>ADR-0060's ordinary Exited proof already releases the operation; a Human release would add nothing.</summary>
    AlreadyFree = 4,

    /// <summary>The sequence did not end Stopped, NeedsAttention or Interrupted (it is live, approved or escalated).</summary>
    SequenceNotEnded = 5,

    /// <summary>No original-host dispatch witness or physical termination observation exists (an old row, a lost result or a missing
    /// optional fact).</summary>
    EvidenceMissing = 6,

    /// <summary>The stored chain is malformed, foreign, duplicated or contradicts the operation.</summary>
    EvidenceIncoherent = 7,
}
