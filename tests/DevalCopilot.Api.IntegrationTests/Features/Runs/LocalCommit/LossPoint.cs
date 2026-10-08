namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>Where a simulated host loss is placed relative to one real external effect of the execution.</summary>
public enum LossPoint
{
    BeforeAcquire,
    BeforePromoteRef,
    AfterPromoteRef,
    BeforePromoteHeldIndex,
    AfterPromoteHeldIndex,
}
