namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalDeliveryReceipt;

/// <summary>The closed answer to "what delivery does this run record?". Only <see cref="Available"/> carries a receipt.</summary>
public enum LocalDeliveryReceiptState
{
    /// <summary>The run has no local-commit operation.</summary>
    NotRecorded = 0,

    /// <summary>The run's operation is recorded but is not a completed delivery.</summary>
    NotCompleted = 1,

    /// <summary>The operation is recorded as completed but its pinned evidence cannot be reconstructed coherently.</summary>
    Unavailable = 2,

    /// <summary>The complete, coherent historical receipt.</summary>
    Available = 3,
}
