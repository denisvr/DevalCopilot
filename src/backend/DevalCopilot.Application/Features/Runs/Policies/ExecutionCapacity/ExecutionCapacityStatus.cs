namespace DevalCopilot.Application.Features.Runs.Policies.ExecutionCapacity;

/// <summary>The closed, derived reading of the recorded potentially mutating execution owners across one database against its persisted limit (ADR-0058, ADR-0059). It states
/// only what one fresh read of existing durable rows found; it is never permission, provider readiness or process liveness.</summary>
public enum ExecutionCapacityStatus
{
    /// <summary>Fewer coherent owners than the persisted limit are recorded, so one further owner may pass this additional gate.</summary>
    Available = 0,

    /// <summary>As many coherent owners as the persisted limit, or more, are recorded; no further owner may be admitted.</summary>
    AtCapacity = 1,

    /// <summary>The recorded ownership cannot be judged safely, so no count is stated.</summary>
    Unavailable = 2,
}
