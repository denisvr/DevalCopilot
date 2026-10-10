namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>Whether the host stopped and every child it started is proven gone, with a closed reason when that is not proven.</summary>
public sealed record ShutdownReport(bool HostStopped, bool ChildrenProven, string? Reason = null)
{
    public static ShutdownReport Unproven { get; } = new(false, false, "Unproven");

    public bool IsProven => HostStopped && ChildrenProven;
}
