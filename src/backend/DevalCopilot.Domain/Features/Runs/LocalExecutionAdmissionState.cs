namespace DevalCopilot.Domain.Features.Runs;

/// <summary>The durable state of one admission of a queued local execution cycle (ADR-0062). The string values are durable schema.</summary>
public enum LocalExecutionAdmissionState
{
    /// <summary>The pending step waits. It owns no operation, preparation token, budget, workspace or global slot.</summary>
    Waiting = 0,

    /// <summary>Its one ordinary Reserved step and original-host preparation token exist; it owns its exact workspace and, when its
    /// step is potentially mutating, one global unit, until it is consumed, retired or left unresolved.</summary>
    Reserved = 1,

    /// <summary>The claim committed: the operation, its link and this transfer were saved atomically. Ownership belongs to the operation.</summary>
    Consumed = 2,

    /// <summary>The admission never became an operation: a Stop, deadline, authority change or restart closed it, and a reservation was
    /// retired only after its unused shape was proved under the write lock.</summary>
    Retired = 3,

    /// <summary>A reservation could not be proved unused or claimed. Its ownership stays held; nothing is retried or released.</summary>
    Unresolved = 4,
}
