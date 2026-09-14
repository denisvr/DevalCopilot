namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The retention classification <c>docs/architecture/data-and-recovery.md</c> requires on every
/// artifact record. This slice records the classification only — no retention or deletion
/// engine exists yet; "deleting a run removes its... artifacts through a dedicated operation"
/// remains a future, explicitly separate feature.
/// </summary>
public enum ArtifactRetentionPolicy
{
    /// <summary>Retained indefinitely until the owning Run is deleted through that future
    /// dedicated operation. The only policy this slice's process-output producer sets.</summary>
    RetainUntilRunDeleted = 0,
}
